using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Common.Security;
using EduFlow.Application.Exams;
using EduFlow.Application.Fees;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Parents;

public sealed class ParentPortalService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    IAuditService auditService,
    ISmsProvider smsProvider,
    IEmailSender emailSender,
    IAppLinks links,
    IFeeService feeService,
    IExamService examService) : IParentPortalService
{
    private static readonly NotificationMessageType[] HiddenMessageTypes =
    [
        NotificationMessageType.PaymentReceivedAdmin,
        NotificationMessageType.BackupResultAdmin,
        NotificationMessageType.DailySummaryAdmin
    ];

    public async Task<IReadOnlyList<ParentAccountDto>> ListParentsAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var parents = await db.Users.AsNoTracking()
            .Where(u => u.RoleType == RoleType.Parent)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);
        return await MapAccountsAsync(parents, cancellationToken);
    }

    public async Task<ParentAccountDto> CreateParentAsync(
        CreateParentAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var fullName = request.FullName?.Trim() ?? string.Empty;
        if (fullName.Length < 2)
        {
            throw new DomainValidationException("NAME_REQUIRED", "Enter the parent's name.");
        }

        var phone = RequirePhone(request.Phone);
        var studentIds = await RequireStudentsAsync(request.StudentIds, cancellationToken);
        var email = ResolveEmail(request.Email, phone, tenantId);
        var sendInvite = request.SendInvite || string.IsNullOrWhiteSpace(request.Password);

        if (!sendInvite)
        {
            PasswordPolicy.ValidateOrThrow(request.Password!);
        }

        var phoneTaken = await db.Users.AnyAsync(
            u => u.RoleType == RoleType.Parent && u.Phone == phone,
            cancellationToken);
        if (phoneTaken)
        {
            throw new DomainValidationException("PHONE_ALREADY_EXISTS", "A parent account already uses this mobile number.", 409);
        }

        var emailTaken = await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower(), cancellationToken);
        if (emailTaken)
        {
            throw new DomainValidationException("EMAIL_ALREADY_EXISTS", "A user with this email already exists in this institute.", 409);
        }

        string? rawToken = null;
        var user = new User
        {
            TenantId = tenantId,
            FullName = fullName,
            Phone = phone,
            Email = email,
            RoleType = RoleType.Parent,
            IsActive = !sendInvite,
            PasswordHash = sendInvite ? string.Empty : PasswordHasher.HashPassword(request.Password!)
        };

        if (sendInvite)
        {
            rawToken = PasswordHasher.GenerateSecureToken(24);
            user.InviteTokenHash = PasswordHasher.HashToken(rawToken);
            user.InviteTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        }

        db.Users.Add(user);
        foreach (var studentId in studentIds)
        {
            db.ParentStudentLinks.Add(new ParentStudentLink
            {
                TenantId = tenantId,
                ParentUserId = user.Id,
                StudentId = studentId
            });
        }

        await auditService.LogAsync(
            "Parent.Created",
            nameof(User),
            user.Id.ToString(),
            new { user.Phone, Invite = sendInvite, StudentCount = studentIds.Count },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (sendInvite && rawToken is not null)
        {
            await SendInviteAsync(user, rawToken, cancellationToken);
        }

        return (await MapAccountsAsync([user], cancellationToken)).Single();
    }

    public async Task<ParentAccountDto> UpdateStudentsAsync(
        Guid parentUserId,
        UpdateParentStudentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var parent = await RequireParentUserAsync(parentUserId, cancellationToken);
        var studentIds = await RequireStudentsAsync(request.StudentIds, cancellationToken);
        await ReplaceLinksAsync(tenantId, parent.Id, studentIds, cancellationToken);
        await auditService.LogAsync(
            "Parent.LinksUpdated",
            nameof(User),
            parent.Id.ToString(),
            new { StudentCount = studentIds.Count },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapAccountsAsync([parent], cancellationToken)).Single();
    }

    public async Task<ParentAccountDto> ResendInviteAsync(Guid parentUserId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var parent = await RequireParentUserAsync(parentUserId, cancellationToken);
        if (parent.IsActive && string.IsNullOrEmpty(parent.InviteTokenHash))
        {
            throw new DomainValidationException("ACCOUNT_ALREADY_ACTIVE", "This parent already has a password. Use forgot password instead.");
        }

        var rawToken = PasswordHasher.GenerateSecureToken(24);
        parent.IsActive = false;
        parent.InviteTokenHash = PasswordHasher.HashToken(rawToken);
        parent.InviteTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
        await auditService.LogAsync(
            "Parent.InviteResent",
            nameof(User),
            parent.Id.ToString(),
            new { parent.Phone },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await SendInviteAsync(parent, rawToken, cancellationToken);
        return (await MapAccountsAsync([parent], cancellationToken)).Single();
    }

    public async Task DeactivateParentAsync(Guid parentUserId, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var parent = await RequireParentUserAsync(parentUserId, cancellationToken);
        parent.IsActive = false;
        await auditService.LogAsync(
            "Parent.Deactivated",
            nameof(User),
            parent.Id.ToString(),
            new { parent.Phone },
            cancellationToken: cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ParentHomeDto> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        var links = await LinkedStudentIdsAsync(parent.Id, cancellationToken);
        var children = await LoadChildrenAsync(links, cancellationToken);
        var caps = await CapabilitiesAsync(parent.TenantId, cancellationToken);
        return new ParentHomeDto(children, caps.CanSeeMarks, caps.CanPayOnline, caps.ReportCardsAvailable);
    }

    public async Task<ParentDashboardDto> GetDashboardAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var child = (await LoadChildrenAsync([studentId], cancellationToken)).Single();
        var today = TodayColombo();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var attendance = await LoadAttendanceAsync(studentId, monthStart, today, cancellationToken);
        var todayItems = attendance.Where(item => item.SessionDate == today).ToList();
        var present = attendance.Count(item => item.Status is "Present" or "Late");
        var classes = await LoadClassesAsync(studentId, today, cancellationToken);
        var exams = await LoadExamsAsync(studentId, parent.TenantId, includeMarks: false, cancellationToken);
        var upcomingExams = exams
            .Where(item => item.Status is nameof(ExamStatus.Scheduled) or nameof(ExamStatus.Locked)
                || (item.ScheduledAt.HasValue && item.ScheduledAt.Value >= DateTimeOffset.UtcNow.AddDays(-1)))
            .Take(5)
            .ToList();
        var invoices = await db.FeeInvoices.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.Status != FeeInvoiceStatus.Voided && i.BalanceAmount > 0m)
            .Select(i => new { i.BalanceAmount, i.Currency })
            .ToListAsync(cancellationToken);
        var messages = await LoadMessagesAsync(parent, [studentId], 1, cancellationToken);
        return new ParentDashboardDto(
            child,
            todayItems,
            present,
            attendance.Count,
            classes.Take(5).ToList(),
            upcomingExams,
            invoices.Count,
            invoices.Sum(i => i.BalanceAmount),
            invoices.FirstOrDefault()?.Currency ?? "LKR",
            messages.FirstOrDefault());
    }

    public async Task<IReadOnlyList<ParentAttendanceItemDto>> GetAttendanceAsync(
        Guid studentId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var today = TodayColombo();
        var start = from ?? today.AddMonths(-3);
        var end = to ?? today;
        return await LoadAttendanceAsync(studentId, start, end, cancellationToken);
    }

    public async Task<IReadOnlyList<ParentClassItemDto>> GetClassesAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        return await LoadClassesAsync(studentId, TodayColombo(), cancellationToken);
    }

    public async Task<IReadOnlyList<ParentExamItemDto>> GetExamsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var caps = await CapabilitiesAsync(parent.TenantId, cancellationToken);
        return await LoadExamsAsync(studentId, parent.TenantId, caps.CanSeeMarks, cancellationToken);
    }

    public async Task<StoredDownload> GetReportCardAsync(Guid studentId, Guid examId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        await RequireFeatureAsync(parent.TenantId, FeatureKey.ParentCanSeeMarks, cancellationToken);
        return await examService.GetReportCardsPdfAsync(examId, studentId, cancellationToken);
    }

    public async Task<IReadOnlyList<ParentInvoiceDto>> GetInvoicesAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var rows = await db.FeeInvoices.AsNoTracking()
            .Where(i => i.StudentId == studentId && i.Status != FeeInvoiceStatus.Voided)
            .OrderByDescending(i => i.DueDate)
            .Take(50)
            .ToListAsync(cancellationToken);
        return rows.Select(i => new ParentInvoiceDto(
            i.Id,
            i.InvoiceNumber,
            i.Title,
            i.BillingPeriod,
            i.NetAmount,
            i.PaidAmount,
            i.BalanceAmount,
            i.Currency,
            i.DueDate,
            i.Status.ToString(),
            i.CreditAppliedAmount)).ToList();
    }

    public async Task<IReadOnlyList<ParentPaymentDto>> GetPaymentsAsync(Guid studentId, CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var payments = await db.FeePayments.AsNoTracking()
            .Where(p => p.StudentId == studentId)
            .OrderByDescending(p => p.PaidAt)
            .Take(50)
            .ToListAsync(cancellationToken);
        var invoiceIds = payments.Select(p => p.InvoiceId).Distinct().ToList();
        var numbers = await db.FeeInvoices.AsNoTracking()
            .Where(i => invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, cancellationToken);
        return payments.Select(p => new ParentPaymentDto(
            p.Id,
            p.ReceiptNumber,
            numbers.GetValueOrDefault(p.InvoiceId, ""),
            p.Amount,
            p.Currency,
            p.Method.ToString(),
            p.Status.ToString(),
            p.PaidAt)).ToList();
    }

    public async Task<StoredDownload> GetPaymentReceiptAsync(
        Guid studentId,
        Guid paymentId,
        bool reprint = false,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var belongs = await db.FeePayments.AsNoTracking()
            .AnyAsync(p => p.Id == paymentId && p.StudentId == studentId, cancellationToken);
        if (!belongs)
        {
            throw new EntityNotFoundException(nameof(FeePayment), paymentId.ToString());
        }

        return await feeService.GetReceiptPdfAsync(paymentId, reprint, cancellationToken);
    }

    public async Task<TenantBankAccountDto> GetBankAccountAsync(CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        return await feeService.GetBankAccountSettingsAsync(parent.TenantId, cancellationToken);
    }

    public async Task<IReadOnlyList<BankPaymentSubmissionDto>> GetBankSlipsAsync(
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        return await feeService.GetBankPaymentSubmissionsAsync(studentId: studentId, cancellationToken: cancellationToken);
    }

    public async Task<BankPaymentSubmissionDto> SubmitBankSlipAsync(
        Guid studentId,
        Guid invoiceId,
        decimal amount,
        string? referenceNumber,
        DateOnly? transferDate,
        string? notes,
        Stream content,
        string fileName,
        string? contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        var ownsInvoice = await db.FeeInvoices.AsNoTracking()
            .AnyAsync(i => i.Id == invoiceId && i.StudentId == studentId, cancellationToken);
        if (!ownsInvoice)
        {
            throw new EntityNotFoundException(nameof(FeeInvoice), invoiceId.ToString());
        }

        return await feeService.SubmitBankPaymentAsync(
            new SubmitBankPaymentMetadata(invoiceId, amount, referenceNumber, transferDate, notes),
            content,
            fileName,
            contentType,
            length,
            submittedByRole: "Parent",
            tenantIdOverride: parent.TenantId,
            cancellationToken: cancellationToken);
    }

    public async Task<StoredDownload> GetBankSlipFileAsync(
        Guid studentId,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        return await feeService.GetBankPaymentSlipAsync(submissionId, studentIdScope: studentId, cancellationToken);
    }

    public async Task<PayHereCheckoutSessionDto> PayInvoiceAsync(
        Guid studentId,
        Guid invoiceId,
        ParentPayInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        await EnsureLinkedAsync(parent.Id, studentId, cancellationToken);
        await RequireFeatureAsync(parent.TenantId, FeatureKey.ParentCanPayOnline, cancellationToken);
        var invoice = await db.FeeInvoices.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invoiceId && i.StudentId == studentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeInvoice), invoiceId.ToString());

        return await feeService.InitiatePayHereCheckoutAsync(
            new InitiatePayHereCheckoutRequest(invoice.Id, request.Amount),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ParentMessageDto>> GetMessagesAsync(CancellationToken cancellationToken = default)
    {
        var parent = await RequireSignedInParentAsync(cancellationToken);
        var studentIds = await LinkedStudentIdsAsync(parent.Id, cancellationToken);
        return await LoadMessagesAsync(parent, studentIds, 50, cancellationToken);
    }

    private async Task SendInviteAsync(User user, string rawToken, CancellationToken cancellationToken)
    {
        var url = links.ParentInviteUrl(user.Phone ?? string.Empty, rawToken);
        var body = $"{user.FullName}, you are invited to the parent portal. Open {url} invite-token:{rawToken}";
        await smsProvider.SendAsync(
            new SmsSendRequest(user.Phone ?? string.Empty, body, TenantId: user.TenantId, IdempotencyKey: $"invite:{user.Id}:{rawToken[..8]}"),
            cancellationToken);

        if (!PhoneNumber.IsPlaceholderEmail(user.Email))
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    user.Email,
                    "Parent portal invitation",
                    $"<p>Open <a href=\"{url}\">{url}</a></p><p>invite-token:{rawToken}</p>",
                    $"Open {url} invite-token:{rawToken}",
                    new Dictionary<string, string> { ["Type"] = "ParentInvite", ["Token"] = rawToken }),
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<ParentAccountDto>> MapAccountsAsync(IReadOnlyList<User> parents, CancellationToken cancellationToken)
    {
        if (parents.Count == 0)
        {
            return [];
        }

        var ids = parents.Select(p => p.Id).ToList();
        var links = await db.ParentStudentLinks.AsNoTracking()
            .Where(l => ids.Contains(l.ParentUserId))
            .ToListAsync(cancellationToken);
        var studentIds = links.Select(l => l.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return parents.Select(parent =>
        {
            var linked = links.Where(l => l.ParentUserId == parent.Id)
                .Select(l => students.GetValueOrDefault(l.StudentId))
                .Where(s => s is not null)
                .Select(s => new ParentLinkedStudentDto(s!.Id, s.FullName, s.StudentCode))
                .OrderBy(s => s.FullName)
                .ToList();
            return new ParentAccountDto(
                parent.Id,
                parent.FullName,
                parent.Phone ?? string.Empty,
                PhoneNumber.IsPlaceholderEmail(parent.Email) ? null : parent.Email,
                parent.IsActive,
                !string.IsNullOrEmpty(parent.InviteTokenHash) && !parent.IsActive,
                linked);
        }).ToList();
    }

    private async Task ReplaceLinksAsync(
        Guid tenantId,
        Guid parentUserId,
        IReadOnlyList<Guid> studentIds,
        CancellationToken cancellationToken)
    {
        var desired = studentIds.ToHashSet();
        var existing = await db.ParentStudentLinks
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.ParentUserId == parentUserId)
            .ToListAsync(cancellationToken);

        foreach (var link in existing)
        {
            link.IsDeleted = !desired.Contains(link.StudentId);
        }

        var have = existing.Select(l => l.StudentId).ToHashSet();
        foreach (var studentId in desired.Where(id => !have.Contains(id)))
        {
            db.ParentStudentLinks.Add(new ParentStudentLink
            {
                TenantId = tenantId,
                ParentUserId = parentUserId,
                StudentId = studentId
            });
        }
    }

    private async Task<User> RequireParentUserAsync(Guid parentUserId, CancellationToken cancellationToken)
    {
        var parent = await db.Users.FirstOrDefaultAsync(
            u => u.Id == parentUserId && u.RoleType == RoleType.Parent,
            cancellationToken);
        return parent ?? throw new EntityNotFoundException(nameof(User), parentUserId.ToString());
    }

    private async Task<User> RequireSignedInParentAsync(CancellationToken cancellationToken)
    {
        if (tenantContext.Role != RoleType.Parent || tenantContext.UserId is null)
        {
            throw new DomainValidationException("PARENT_ONLY", "This area is for parent accounts.", 403);
        }

        var parent = await db.Users.AsNoTracking().FirstOrDefaultAsync(
            u => u.Id == tenantContext.UserId.Value && u.RoleType == RoleType.Parent && u.IsActive,
            cancellationToken);
        return parent ?? throw new DomainValidationException("PARENT_ONLY", "This parent account is not active.", 403);
    }

    private async Task<List<Guid>> LinkedStudentIdsAsync(Guid parentUserId, CancellationToken cancellationToken) =>
        await db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentUserId == parentUserId)
            .Select(l => l.StudentId)
            .ToListAsync(cancellationToken);

    private async Task EnsureLinkedAsync(Guid parentUserId, Guid studentId, CancellationToken cancellationToken)
    {
        var linked = await db.ParentStudentLinks.AnyAsync(
            l => l.ParentUserId == parentUserId && l.StudentId == studentId,
            cancellationToken);
        if (!linked)
        {
            throw new EntityNotFoundException(nameof(Student), studentId.ToString());
        }
    }

    private async Task<List<Guid>> RequireStudentsAsync(IReadOnlyList<Guid>? studentIds, CancellationToken cancellationToken)
    {
        var ids = (studentIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            throw new DomainValidationException("STUDENTS_REQUIRED", "Link at least one student.");
        }

        var found = await db.Students.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
        if (found.Count != ids.Count)
        {
            throw new EntityNotFoundException(nameof(Student));
        }

        return ids;
    }

    private static string RequirePhone(string? raw)
    {
        var phone = PhoneNumber.Normalize(raw);
        if (phone.Length == 0)
        {
            throw new DomainValidationException("INVALID_PHONE", "Enter a valid mobile number.");
        }

        return phone;
    }

    private static string ResolveEmail(string? email, string phone, Guid tenantId)
    {
        var trimmed = email?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return PhoneNumber.PlaceholderEmail(phone, tenantId);
        }

        if (!trimmed.Contains('@') || !trimmed.Contains('.'))
        {
            throw new DomainValidationException("INVALID_EMAIL", "Enter a valid email address or leave it blank.");
        }

        return trimmed.ToLowerInvariant();
    }

    private async Task<(bool CanSeeMarks, bool CanPayOnline, bool ReportCardsAvailable)> CapabilitiesAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var marks = await featureService.EvaluateAsync(tenantId, FeatureKey.ParentCanSeeMarks, cancellationToken);
        var pay = await featureService.EvaluateAsync(tenantId, FeatureKey.ParentCanPayOnline, cancellationToken);
        var online = await featureService.EvaluateAsync(tenantId, FeatureKey.OnlinePayments, cancellationToken);
        var reports = await featureService.EvaluateAsync(tenantId, FeatureKey.PerformanceReports, cancellationToken);
        return (marks.IsEnabled, pay.IsEnabled && online.IsEnabled, marks.IsEnabled && reports.IsEnabled);
    }

    private async Task RequireFeatureAsync(Guid tenantId, FeatureKey key, CancellationToken cancellationToken)
    {
        var evaluation = await featureService.EvaluateAsync(tenantId, key, cancellationToken);
        if (!evaluation.IsInPlan)
        {
            throw new FeatureNotInPlanException(key);
        }

        if (!evaluation.IsEnabled)
        {
            throw new FeatureDisabledException(key);
        }
    }

    private async Task<IReadOnlyList<ParentChildDto>> LoadChildrenAsync(IReadOnlyList<Guid> studentIds, CancellationToken cancellationToken)
    {
        if (studentIds.Count == 0)
        {
            return [];
        }

        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToListAsync(cancellationToken);
        var enrollments = await db.StudentEnrollments.AsNoTracking()
            .Where(e => studentIds.Contains(e.StudentId) && e.IsActive)
            .ToListAsync(cancellationToken);
        var batchIds = enrollments.Select(e => e.BatchId).Distinct().ToList();
        var batches = await db.AcademicBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);
        var levelIds = students.Select(s => s.EducationLevelId).Distinct().ToList();
        var levels = await db.EducationLevels.AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);
        var order = studentIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);

        return students
            .OrderBy(s => order.GetValueOrDefault(s.Id, 0))
            .Select(s =>
            {
                var names = enrollments.Where(e => e.StudentId == s.Id)
                    .Select(e => batches.GetValueOrDefault(e.BatchId))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .ToList();
                return new ParentChildDto(
                    s.Id,
                    s.FullName,
                    s.StudentCode,
                    s.PhotoUrl,
                    levels.GetValueOrDefault(s.EducationLevelId, ""),
                    string.Join(", ", names));
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ParentAttendanceItemDto>> LoadAttendanceAsync(
        Guid studentId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var marks = await db.AttendanceMarks.AsNoTracking()
            .Where(m => m.StudentId == studentId && !m.IsUndone)
            .ToListAsync(cancellationToken);
        if (marks.Count == 0)
        {
            return [];
        }

        var sessionIds = marks.Select(m => m.ClassSessionId).Distinct().ToList();
        var sessions = await db.ClassSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id) && s.SessionDate >= from && s.SessionDate <= to)
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var scheduleIds = sessions.Values.Select(s => s.ClassScheduleId).Distinct().ToList();
        var schedules = await db.ClassSchedules.AsNoTracking()
            .Where(s => scheduleIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);
        var subjectIds = schedules.Values.Select(s => s.SubjectId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking()
            .Where(s => subjectIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        return marks
            .Where(m => sessions.ContainsKey(m.ClassSessionId))
            .Select(m =>
            {
                var session = sessions[m.ClassSessionId];
                var schedule = schedules.GetValueOrDefault(session.ClassScheduleId);
                return new ParentAttendanceItemDto(
                    m.Id,
                    session.SessionDate,
                    schedule?.Name ?? "Class",
                    schedule is null ? "" : subjects.GetValueOrDefault(schedule.SubjectId, ""),
                    m.Status.ToString(),
                    m.MarkedAt);
            })
            .OrderByDescending(item => item.SessionDate)
            .ThenByDescending(item => item.MarkedAt)
            .ToList();
    }

    private async Task<IReadOnlyList<ParentClassItemDto>> LoadClassesAsync(
        Guid studentId,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var batchIds = await db.StudentEnrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.IsActive)
            .Select(e => e.BatchId)
            .ToListAsync(cancellationToken);
        var schedules = await db.ClassSchedules.AsNoTracking()
            .Where(s => s.IsActive && batchIds.Contains(s.BatchId))
            .OrderBy(s => s.Weekday)
            .ThenBy(s => s.StartMinutes)
            .ToListAsync(cancellationToken);
        if (schedules.Count == 0)
        {
            return [];
        }

        var subjectIds = schedules.Select(s => s.SubjectId).Distinct().ToList();
        var teacherIds = schedules.Select(s => s.TeacherUserId).Distinct().ToList();
        var batchIdSet = schedules.Select(s => s.BatchId).Distinct().ToList();
        var subjects = await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        var teachers = await db.Users.AsNoTracking().Where(u => teacherIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
        var batches = await db.AcademicBatches.AsNoTracking().Where(b => batchIdSet.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return schedules.Select(schedule => new ParentClassItemDto(
                schedule.Id,
                schedule.Name,
                subjects.GetValueOrDefault(schedule.SubjectId, ""),
                teachers.GetValueOrDefault(schedule.TeacherUserId, ""),
                batches.GetValueOrDefault(schedule.BatchId, ""),
                schedule.Weekday,
                schedule.StartMinutes,
                schedule.EndMinutes,
                schedule.Room,
                NextDate(today, schedule.Weekday)))
            .OrderBy(item => item.NextDate)
            .ThenBy(item => item.StartMinutes)
            .ToList();
    }

    private async Task<IReadOnlyList<ParentExamItemDto>> LoadExamsAsync(
        Guid studentId,
        Guid tenantId,
        bool includeMarks,
        CancellationToken cancellationToken)
    {
        var batchIds = await db.StudentEnrollments.AsNoTracking()
            .Where(e => e.StudentId == studentId && e.IsActive)
            .Select(e => e.BatchId)
            .ToListAsync(cancellationToken);
        var exams = await db.Exams.AsNoTracking()
            .Where(e => batchIds.Contains(e.BatchId) && e.Status != ExamStatus.Draft)
            .OrderByDescending(e => e.ScheduledAt)
            .Take(30)
            .ToListAsync(cancellationToken);
        if (exams.Count == 0)
        {
            return [];
        }

        var canShow = includeMarks && (await featureService.IsEnabledAsync(tenantId, FeatureKey.ParentCanSeeMarks, cancellationToken));
        var examIds = exams.Select(e => e.Id).ToList();
        var results = canShow
            ? await db.ExamResults.AsNoTracking()
                .Where(r => r.StudentId == studentId && examIds.Contains(r.ExamId))
                .ToDictionaryAsync(r => r.ExamId, cancellationToken)
            : new Dictionary<Guid, ExamResult>();
        var papers = canShow
            ? await db.ExamSubjects.AsNoTracking().Where(s => examIds.Contains(s.ExamId)).ToListAsync(cancellationToken)
            : [];
        var marks = canShow
            ? await db.ExamMarks.AsNoTracking()
                .Where(m => m.StudentId == studentId && examIds.Contains(m.ExamId))
                .ToListAsync(cancellationToken)
            : [];
        var subjectIds = papers.Select(p => p.SubjectId).Distinct().ToList();
        var subjects = subjectIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Subjects.AsNoTracking().Where(s => subjectIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        return exams.Select(exam =>
        {
            results.TryGetValue(exam.Id, out var result);
            var show = canShow && exam.Status == ExamStatus.Published && result is not null;
            IReadOnlyList<ParentExamSubjectDto>? lines = null;
            if (show)
            {
                lines = papers.Where(p => p.ExamId == exam.Id).OrderBy(p => p.SortOrder).Select(paper =>
                {
                    var mark = marks.FirstOrDefault(m => m.ExamSubjectId == paper.Id);
                    return new ParentExamSubjectDto(
                        subjects.GetValueOrDefault(paper.SubjectId, "Subject"),
                        paper.MaxMarks,
                        mark?.Marks,
                        mark?.IsAbsent ?? false,
                        mark?.Grade,
                        mark?.SubjectRank);
                }).ToList();
            }

            return new ParentExamItemDto(
                exam.Id,
                exam.Name,
                exam.Kind.ToString(),
                exam.Status.ToString(),
                exam.ScheduledAt,
                exam.Room,
                show ? result!.Percentage : null,
                show ? result!.Grade : null,
                show ? result!.BatchRank : null,
                show ? result!.LevelRank : null,
                lines);
        }).ToList();
    }

    private async Task<IReadOnlyList<ParentMessageDto>> LoadMessagesAsync(
        User parent,
        IReadOnlyList<Guid> studentIds,
        int take,
        CancellationToken cancellationToken)
    {
        var phone = parent.Phone ?? string.Empty;
        var rows = await db.NotificationOutboxes.AsNoTracking()
            .Where(m => !HiddenMessageTypes.Contains(m.MessageType))
            .Where(m =>
                (m.StudentId.HasValue && studentIds.Contains(m.StudentId.Value))
                || (m.MessageType == NotificationMessageType.Broadcast && phone.Length > 0 && m.Recipient == phone))
            .OrderByDescending(m => m.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return rows.Select(m => new ParentMessageDto(
            m.Id,
            m.MessageType.ToString(),
            m.Channel.ToString(),
            m.Status.ToString(),
            m.Subject,
            m.Body,
            m.StudentName,
            m.CreatedAt,
            m.SentAt)).ToList();
    }

    private static DateOnly TodayColombo()
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById("Asia/Colombo", out var zone))
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Sri Lanka Standard Time");
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
    }

    private static DateOnly NextDate(DateOnly today, int weekday)
    {
        var current = today.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)today.DayOfWeek;
        var delta = (weekday - current + 7) % 7;
        return today.AddDays(delta);
    }
}
