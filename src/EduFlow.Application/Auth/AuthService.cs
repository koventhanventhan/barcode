using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Domain.Constants;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
    Task<InviteUserResponse> InviteUserAsync(InviteUserRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse> AcceptInviteAsync(AcceptInviteRequest request, CancellationToken cancellationToken = default);
}

public sealed class AuthService(
    IAppDbContext db,
    ITenantContext tenantContext,
    ITokenService tokenService,
    IFeatureService featureService,
    ILimitService limitService,
    IEmailSender emailSender,
    IAuditService auditService) : IAuthService
{
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        User? user;
        if (tenantContext.TenantId.HasValue && tenantContext.TenantId.Value != Guid.Empty)
        {
            var tid = tenantContext.TenantId.Value;
            user = await db.Users
                .IgnoreQueryFilters()
                .Include(u => u.CustomRole)
                .ThenInclude(r => r!.Permissions)
                .FirstOrDefaultAsync(
                    u => !u.IsDeleted && u.TenantId == tid && u.Email.ToLower() == normalizedEmail,
                    cancellationToken);
        }
        else
        {
            user = await db.Users
                .IgnoreQueryFilters()
                .Include(u => u.CustomRole)
                .ThenInclude(r => r!.Permissions)
                .FirstOrDefaultAsync(
                    u => !u.IsDeleted && u.RoleType == RoleType.SuperAdmin && u.Email.ToLower() == normalizedEmail,
                    cancellationToken);

            user ??= await db.Users
                .IgnoreQueryFilters()
                .Include(u => u.CustomRole)
                .ThenInclude(r => r!.Permissions)
                .FirstOrDefaultAsync(
                    u => !u.IsDeleted && u.Email.ToLower() == normalizedEmail,
                    cancellationToken);
        }

        if (user is null || !user.IsActive)
        {
            throw new DomainValidationException("INVALID_CREDENTIALS", "Invalid email or password.", 401);
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now)
        {
            throw new AccountLockedException(user.LockoutEnd.Value);
        }

        if (!PasswordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                {
                    user.LockoutEnd = now.Add(LockoutDuration);
                    await auditService.LogAsync(
                        "Auth.UserLockedOut",
                        nameof(User),
                        user.Id.ToString(),
                        new { user.Email, user.FailedLoginAttempts, user.LockoutEnd },
                        tenantIdOverride: user.TenantId == Guid.Empty ? null : user.TenantId,
                        actorUserIdOverride: user.Id,
                        cancellationToken: cancellationToken);
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > now)
            {
                throw new AccountLockedException(user.LockoutEnd.Value);
            }

            throw new DomainValidationException("INVALID_CREDENTIALS", "Invalid email or password.", 401);
        }

        if (user.RoleType == RoleType.Parent && user.TenantId != Guid.Empty)
        {
            var evaluation = await featureService.EvaluateAsync(user.TenantId, FeatureKey.ParentPortal, cancellationToken);
            if (!evaluation.IsInPlan)
            {
                throw new FeatureNotInPlanException(FeatureKey.ParentPortal);
            }

            if (!evaluation.IsEnabled)
            {
                throw new FeatureDisabledException(FeatureKey.ParentPortal);
            }
        }

        return await IssueTokensForUserAsync(user, familyId: Guid.NewGuid(), cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new DomainValidationException("INVALID_REFRESH_TOKEN", "Refresh token is required.", 401);
        }

        var tokenHash = PasswordHasher.HashToken(refreshToken);
        var existingToken = await db.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(rt => !rt.IsDeleted && rt.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            throw new DomainValidationException("INVALID_REFRESH_TOKEN", "Refresh token is invalid.", 401);
        }

        var now = DateTimeOffset.UtcNow;

        if (existingToken.RevokedAt.HasValue)
        {
            using (tenantContext.BeginSystemScope(existingToken.TenantId == Guid.Empty ? null : existingToken.TenantId))
            {
                var familyTokens = await db.RefreshTokens
                    .IgnoreQueryFilters()
                    .Where(rt => !rt.IsDeleted && rt.FamilyId == existingToken.FamilyId && rt.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var activeToken in familyTokens)
                {
                    activeToken.RevokedAt = now;
                    activeToken.RevokedReason = "TokenReuseDetected";
                }

                await auditService.LogAsync(
                    "Auth.RefreshTokenReuseDetected",
                    nameof(RefreshToken),
                    existingToken.Id.ToString(),
                    new { existingToken.UserId, existingToken.FamilyId },
                    tenantIdOverride: existingToken.TenantId == Guid.Empty ? null : existingToken.TenantId,
                    actorUserIdOverride: existingToken.UserId,
                    cancellationToken: cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
            }

            throw new DomainValidationException("REFRESH_TOKEN_REUSED", "Refresh token has already been used. All sessions in this family have been revoked.", 401);
        }

        if (existingToken.ExpiresAt <= now)
        {
            throw new DomainValidationException("REFRESH_TOKEN_EXPIRED", "Refresh token has expired.", 401);
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .Include(u => u.CustomRole)
            .ThenInclude(r => r!.Permissions)
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Id == existingToken.UserId && u.IsActive, cancellationToken);

        if (user is null)
        {
            throw new DomainValidationException("INVALID_REFRESH_TOKEN", "User account is not active.", 401);
        }

        if (user.RoleType == RoleType.Parent && user.TenantId != Guid.Empty)
        {
            var evaluation = await featureService.EvaluateAsync(user.TenantId, FeatureKey.ParentPortal, cancellationToken);
            if (!evaluation.IsEnabled)
            {
                throw new FeatureDisabledException(FeatureKey.ParentPortal);
            }
        }

        var newRawRefreshToken = PasswordHasher.GenerateSecureToken();
        var newRefreshTokenHash = PasswordHasher.HashToken(newRawRefreshToken);

        using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
        {
            existingToken.RevokedAt = now;
            existingToken.RevokedReason = "Rotated";
            existingToken.ReplacedByTokenHash = newRefreshTokenHash;

            var rotatedEntity = new RefreshToken
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                TokenHash = newRefreshTokenHash,
                FamilyId = existingToken.FamilyId,
                ExpiresAt = now.Add(RefreshTokenLifetime),
                CreatedByIp = tenantContext.IpAddress
            };

            db.RefreshTokens.Add(rotatedEntity);
            await db.SaveChangesAsync(cancellationToken);
        }

        var permissions = await ResolvePermissionsAsync(user, cancellationToken);
        var tenantSubdomain = await ResolveTenantSubdomainAsync(user.TenantId, cancellationToken);
        var generatedJwt = tokenService.GenerateAccessToken(user, permissions, tenantSubdomain);

        return new AuthResponse(
            generatedJwt.Token,
            newRawRefreshToken,
            generatedJwt.ExpiresAt,
            new AuthUserDto(
                user.Id,
                user.TenantId == Guid.Empty ? null : user.TenantId,
                user.Email,
                user.FullName,
                user.RoleType.ToString(),
                permissions,
                user.MustChangePassword));
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        using (tenantContext.BeginSystemScope(tenantContext.TenantId))
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var hash = PasswordHasher.HashToken(refreshToken);
                var token = await db.RefreshTokens
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(rt => !rt.IsDeleted && rt.TokenHash == hash, cancellationToken);

                if (token is not null && token.RevokedAt is null)
                {
                    token.RevokedAt = now;
                    token.RevokedReason = "LoggedOut";
                }
            }
            else if (tenantContext.UserId.HasValue)
            {
                var uid = tenantContext.UserId.Value;
                var activeTokens = await db.RefreshTokens
                    .IgnoreQueryFilters()
                    .Where(rt => !rt.IsDeleted && rt.UserId == uid && rt.RevokedAt == null)
                    .ToListAsync(cancellationToken);

                foreach (var token in activeTokens)
                {
                    token.RevokedAt = now;
                    token.RevokedReason = "LoggedOut";
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (!tenantContext.UserId.HasValue)
        {
            throw new DomainValidationException("UNAUTHORIZED", "Authentication required.", 401);
        }

        PasswordPolicy.ValidateOrThrow(request.NewPassword);

        var uid = tenantContext.UserId.Value;
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Id == uid, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(User), uid.ToString());

        if (!PasswordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            throw new DomainValidationException("INVALID_CURRENT_PASSWORD", "Current password is incorrect.", 400);
        }

        using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
        {
            user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            user.MustChangePassword = false;

            var activeTokens = await db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => !rt.IsDeleted && rt.UserId == user.Id && rt.RevokedAt == null)
                .ToListAsync(cancellationToken);

            var now = DateTimeOffset.UtcNow;
            foreach (var token in activeTokens)
            {
                token.RevokedAt = now;
                token.RevokedReason = "PasswordChanged";
            }

            await auditService.LogAsync(
                "Auth.PasswordChanged",
                nameof(User),
                user.Id.ToString(),
                tenantIdOverride: user.TenantId == Guid.Empty ? null : user.TenantId,
                actorUserIdOverride: user.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        User? user;
        if (tenantContext.TenantId.HasValue && tenantContext.TenantId.Value != Guid.Empty)
        {
            var tid = tenantContext.TenantId.Value;
            user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => !u.IsDeleted && u.TenantId == tid && u.Email.ToLower() == normalizedEmail, cancellationToken);
        }
        else
        {
            user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => !u.IsDeleted && u.Email.ToLower() == normalizedEmail, cancellationToken);
        }

        if (user is null || !user.IsActive)
        {
            return;
        }

        var rawToken = PasswordHasher.GenerateSecureToken(24);
        var tokenHash = PasswordHasher.HashToken(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
        {
            user.PasswordResetTokenHash = tokenHash;
            user.PasswordResetTokenExpiresAt = expiresAt;
            await db.SaveChangesAsync(cancellationToken);
        }

        await emailSender.SendAsync(
            new EmailMessage(
                To: user.Email,
                Subject: "Reset your password",
                HtmlBody: $"<p>Use token <code>{rawToken}</code> to reset your password before {expiresAt:O}.</p>",
                PlainTextBody: $"Use token {rawToken} to reset your password.",
                Metadata: new Dictionary<string, string>
                {
                    ["Type"] = "PasswordReset",
                    ["Token"] = rawToken,
                    ["Email"] = user.Email
                }),
            cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        PasswordPolicy.ValidateOrThrow(request.NewPassword);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var tokenHash = PasswordHasher.HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;

        User? user;
        if (tenantContext.TenantId.HasValue && tenantContext.TenantId.Value != Guid.Empty)
        {
            var tid = tenantContext.TenantId.Value;
            user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => !u.IsDeleted && u.TenantId == tid && u.Email.ToLower() == normalizedEmail, cancellationToken);
        }
        else
        {
            user = await db.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => !u.IsDeleted && u.Email.ToLower() == normalizedEmail, cancellationToken);
        }

        if (user is null
            || string.IsNullOrEmpty(user.PasswordResetTokenHash)
            || user.PasswordResetTokenHash != tokenHash
            || !user.PasswordResetTokenExpiresAt.HasValue
            || user.PasswordResetTokenExpiresAt.Value < now)
        {
            throw new DomainValidationException("INVALID_RESET_TOKEN", "Invalid or expired password reset token.", 400);
        }

        using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
        {
            user.PasswordHash = PasswordHasher.HashPassword(request.NewPassword);
            user.PasswordResetTokenHash = null;
            user.PasswordResetTokenExpiresAt = null;
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.MustChangePassword = false;

            var activeTokens = await db.RefreshTokens
                .IgnoreQueryFilters()
                .Where(rt => !rt.IsDeleted && rt.UserId == user.Id && rt.RevokedAt == null)
                .ToListAsync(cancellationToken);

            foreach (var rt in activeTokens)
            {
                rt.RevokedAt = now;
                rt.RevokedReason = "PasswordReset";
            }

            await auditService.LogAsync(
                "Auth.PasswordReset",
                nameof(User),
                user.Id.ToString(),
                tenantIdOverride: user.TenantId == Guid.Empty ? null : user.TenantId,
                actorUserIdOverride: user.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<InviteUserResponse> InviteUserAsync(
        InviteUserRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty)
        {
            throw new DomainValidationException("TENANT_REQUIRED", "A tenant context is required to invite users.", 400);
        }

        if (request.RoleType == RoleType.SuperAdmin)
        {
            throw new DomainValidationException("INVALID_ROLE", "Cannot invite SuperAdmin inside a tenant.", 400);
        }

        var tenantId = tenantContext.TenantId.Value;
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existing = await db.Users
            .AnyAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (existing)
        {
            throw new DomainValidationException("EMAIL_ALREADY_EXISTS", "A user with this email already exists in this institute.", 409);
        }

        if (request.RoleType == RoleType.InstituteAdmin)
        {
            await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.AdminAccounts, cancellationToken: cancellationToken);
        }
        else if (request.RoleType is RoleType.Staff or RoleType.Teacher)
        {
            await limitService.EnsureWithinLimitAsync(tenantId, LimitKey.StaffAccounts, cancellationToken: cancellationToken);
        }

        if (request.CustomRoleId.HasValue)
        {
            var roleExists = await db.Roles.AnyAsync(r => r.Id == request.CustomRoleId.Value, cancellationToken);
            if (!roleExists)
            {
                throw new EntityNotFoundException(nameof(Role), request.CustomRoleId.Value.ToString());
            }
        }

        var rawToken = PasswordHasher.GenerateSecureToken(24);
        var tokenHash = PasswordHasher.HashToken(rawToken);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        var user = new User
        {
            TenantId = tenantId,
            Email = normalizedEmail,
            FullName = request.FullName.Trim(),
            PasswordHash = string.Empty,
            RoleType = request.RoleType,
            CustomRoleId = request.CustomRoleId,
            BranchId = request.BranchId,
            IsActive = false,
            InviteTokenHash = tokenHash,
            InviteTokenExpiresAt = expiresAt
        };

        db.Users.Add(user);

        await auditService.LogAsync(
            "User.Invited",
            nameof(User),
            user.Id.ToString(),
            new { user.Email, Role = user.RoleType.ToString(), user.CustomRoleId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        await emailSender.SendAsync(
            new EmailMessage(
                To: user.Email,
                Subject: "You have been invited to join EduFlow",
                HtmlBody: $"<p>Hello {user.FullName}, accept your invitation with token <code>{rawToken}</code>.</p>",
                PlainTextBody: $"Accept your invitation with token {rawToken}.",
                Metadata: new Dictionary<string, string>
                {
                    ["Type"] = "UserInvite",
                    ["Token"] = rawToken,
                    ["Email"] = user.Email,
                    ["TenantId"] = tenantId.ToString()
                }),
            cancellationToken);

        return new InviteUserResponse(user.Id, user.Email, expiresAt);
    }

    public async Task<AuthResponse> AcceptInviteAsync(
        AcceptInviteRequest request,
        CancellationToken cancellationToken = default)
    {
        PasswordPolicy.ValidateOrThrow(request.Password);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var tokenHash = PasswordHasher.HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;

        User? user;
        if (tenantContext.TenantId.HasValue && tenantContext.TenantId.Value != Guid.Empty)
        {
            var tid = tenantContext.TenantId.Value;
            user = await db.Users
                .IgnoreQueryFilters()
                .Include(u => u.CustomRole)
                .ThenInclude(r => r!.Permissions)
                .FirstOrDefaultAsync(
                    u => !u.IsDeleted && u.TenantId == tid && u.Email.ToLower() == normalizedEmail,
                    cancellationToken);
        }
        else
        {
            user = await db.Users
                .IgnoreQueryFilters()
                .Include(u => u.CustomRole)
                .ThenInclude(r => r!.Permissions)
                .FirstOrDefaultAsync(
                    u => !u.IsDeleted && u.Email.ToLower() == normalizedEmail && u.InviteTokenHash == tokenHash,
                    cancellationToken);
        }

        if (user is null
            || string.IsNullOrEmpty(user.InviteTokenHash)
            || user.InviteTokenHash != tokenHash
            || !user.InviteTokenExpiresAt.HasValue
            || user.InviteTokenExpiresAt.Value < now)
        {
            throw new DomainValidationException("INVALID_INVITE_TOKEN", "Invalid or expired invitation token.", 400);
        }

        using (tenantContext.BeginSystemScope(user.TenantId))
        {
            if (!string.IsNullOrWhiteSpace(request.FullName))
            {
                user.FullName = request.FullName.Trim();
            }

            user.PasswordHash = PasswordHasher.HashPassword(request.Password);
            user.IsActive = true;
            user.InviteTokenHash = null;
            user.InviteTokenExpiresAt = null;

            await auditService.LogAsync(
                "User.InviteAccepted",
                nameof(User),
                user.Id.ToString(),
                tenantIdOverride: user.TenantId,
                actorUserIdOverride: user.Id,
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        return await IssueTokensForUserAsync(user, familyId: Guid.NewGuid(), cancellationToken);
    }

    private async Task<AuthResponse> IssueTokensForUserAsync(
        User user,
        Guid familyId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rawRefreshToken = PasswordHasher.GenerateSecureToken();
        var refreshTokenHash = PasswordHasher.HashToken(rawRefreshToken);

        using (tenantContext.BeginSystemScope(user.TenantId == Guid.Empty ? null : user.TenantId))
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            user.LastLoginAt = now;

            var refreshToken = new RefreshToken
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                FamilyId = familyId,
                ExpiresAt = now.Add(RefreshTokenLifetime),
                CreatedByIp = tenantContext.IpAddress
            };

            db.RefreshTokens.Add(refreshToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var permissions = await ResolvePermissionsAsync(user, cancellationToken);
        var tenantSubdomain = await ResolveTenantSubdomainAsync(user.TenantId, cancellationToken);
        var generatedJwt = tokenService.GenerateAccessToken(user, permissions, tenantSubdomain);

        return new AuthResponse(
            generatedJwt.Token,
            rawRefreshToken,
            generatedJwt.ExpiresAt,
            new AuthUserDto(
                user.Id,
                user.TenantId == Guid.Empty ? null : user.TenantId,
                user.Email,
                user.FullName,
                user.RoleType.ToString(),
                permissions,
                user.MustChangePassword));
    }

    private async Task<IReadOnlyList<string>> ResolvePermissionsAsync(User user, CancellationToken cancellationToken)
    {
        if (user.RoleType is RoleType.SuperAdmin or RoleType.InstituteAdmin)
        {
            return Permissions.All.Select(p => p.Key).ToList();
        }

        if (user.CustomRoleId.HasValue)
        {
            if (user.CustomRole is not null && user.CustomRole.Permissions.Count > 0)
            {
                return user.CustomRole.Permissions.Where(p => !p.IsDeleted).Select(p => p.PermissionKey).Distinct().ToList();
            }

            return await db.RolePermissions
                .IgnoreQueryFilters()
                .Where(rp => !rp.IsDeleted && rp.RoleId == user.CustomRoleId.Value)
                .Select(rp => rp.PermissionKey)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        return user.RoleType switch
        {
            RoleType.Staff => Permissions.DefaultStaffPermissions,
            RoleType.Teacher => Permissions.DefaultTeacherPermissions,
            _ => []
        };
    }

    private async Task<string?> ResolveTenantSubdomainAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
        {
            return null;
        }

        return await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => !t.IsDeleted && t.Id == tenantId)
            .Select(t => t.Subdomain)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
