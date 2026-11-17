using System.Collections.Concurrent;
using System.Globalization;
using EduFlow.Application.Academics;
using EduFlow.Application.Common.Interfaces;
using EduFlow.Application.Notifications;
using EduFlow.Domain.Entities;
using EduFlow.Domain.Enums;
using EduFlow.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace EduFlow.Application.Fees;

public sealed class FeeService(
    IAppDbContext db,
    ITenantContext tenantContext,
    IFeatureService featureService,
    ILimitService limitService,
    INotificationService notificationService,
    INotificationDispatcher notificationDispatcher,
    IPaymentGateway paymentGateway,
    IFeeDocumentService feeDocuments,
    IWorkbookFile workbook,
    IFileStorage fileStorage,
    IAuditService auditService) : IFeeService
{
    private const long MaxAttachmentBytes = 5 * 1024 * 1024;
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> TenantLocks = new();

    private static SemaphoreSlim GetTenantLock(Guid tenantId) =>
        TenantLocks.GetOrAdd(tenantId, _ => new SemaphoreSlim(1, 1));

    public async Task<IReadOnlyList<FeeStructureDto>> GetFeeStructuresAsync(CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var structures = await db.FeeStructures
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        return await MapFeeStructuresAsync(structures, cancellationToken);
    }

    public async Task<FeeStructureDto> CreateFeeStructureAsync(
        UpsertFeeStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var (name, amount, currency, levelId, batchId, dueDay, description) = await ValidateFeeStructureAsync(request, cancellationToken);

        var structure = new FeeStructure
        {
            TenantId = tenantId,
            Name = name,
            Frequency = request.Frequency,
            Amount = amount,
            Currency = currency,
            EducationLevelId = levelId,
            BatchId = batchId,
            DueDayOfMonth = dueDay,
            Description = description,
            IsActive = request.IsActive
        };

        db.FeeStructures.Add(structure);
        await auditService.LogAsync(
            "FeeStructure.Created",
            nameof(FeeStructure),
            structure.Id.ToString(),
            new { structure.Name, Frequency = structure.Frequency.ToString(), structure.Amount, structure.BatchId, structure.EducationLevelId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapFeeStructuresAsync([structure], cancellationToken)).Single();
    }

    public async Task<FeeStructureDto> UpdateFeeStructureAsync(
        Guid id,
        UpsertFeeStructureRequest request,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var structure = await db.FeeStructures.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeStructure), id.ToString());

        var (name, amount, currency, levelId, batchId, dueDay, description) = await ValidateFeeStructureAsync(request, cancellationToken);

        structure.Name = name;
        structure.Frequency = request.Frequency;
        structure.Amount = amount;
        structure.Currency = currency;
        structure.EducationLevelId = levelId;
        structure.BatchId = batchId;
        structure.DueDayOfMonth = dueDay;
        structure.Description = description;
        structure.IsActive = request.IsActive;

        await auditService.LogAsync(
            "FeeStructure.Updated",
            nameof(FeeStructure),
            structure.Id.ToString(),
            new { structure.Name, Frequency = structure.Frequency.ToString(), structure.Amount, structure.IsActive },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapFeeStructuresAsync([structure], cancellationToken)).Single();
    }

    public async Task DeleteFeeStructureAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var structure = await db.FeeStructures.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeStructure), id.ToString());

        var hasActiveInvoices = await db.FeeInvoices
            .AnyAsync(i => i.FeeStructureId == id && i.Status != FeeInvoiceStatus.Voided, cancellationToken);
        if (hasActiveInvoices)
        {
            throw new DomainValidationException(
                "FEE_STRUCTURE_IN_USE",
                "Cannot delete a fee structure that has active invoices. Deactivate it instead.",
                409);
        }

        db.FeeStructures.Remove(structure);
        await auditService.LogAsync(
            "FeeStructure.Deleted",
            nameof(FeeStructure),
            structure.Id.ToString(),
            new { structure.Name },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudentConcessionDto>> GetConcessionsAsync(
        Guid? studentId = null,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var query = db.StudentConcessions.AsNoTracking();
        if (studentId.HasValue)
        {
            query = query.Where(c => c.StudentId == studentId.Value);
        }

        var items = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        return await MapConcessionsAsync(items, cancellationToken);
    }

    public async Task<StudentConcessionDto> CreateConcessionAsync(
        UpsertStudentConcessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var (name, value, reason) = await ValidateConcessionAsync(request, cancellationToken);

        var concession = new StudentConcession
        {
            TenantId = tenantId,
            StudentId = request.StudentId,
            FeeStructureId = request.FeeStructureId,
            Name = name,
            DiscountType = request.DiscountType,
            Value = value,
            Reason = reason,
            ValidFrom = request.ValidFrom,
            ValidTo = request.ValidTo,
            IsActive = request.IsActive
        };

        db.StudentConcessions.Add(concession);
        await auditService.LogAsync(
            "StudentConcession.Created",
            nameof(StudentConcession),
            concession.Id.ToString(),
            new { concession.StudentId, concession.Name, DiscountType = concession.DiscountType.ToString(), concession.Value },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapConcessionsAsync([concession], cancellationToken)).Single();
    }

    public async Task<StudentConcessionDto> UpdateConcessionAsync(
        Guid id,
        UpsertStudentConcessionRequest request,
        CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var concession = await db.StudentConcessions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(StudentConcession), id.ToString());

        var (name, value, reason) = await ValidateConcessionAsync(request, cancellationToken);

        concession.StudentId = request.StudentId;
        concession.FeeStructureId = request.FeeStructureId;
        concession.Name = name;
        concession.DiscountType = request.DiscountType;
        concession.Value = value;
        concession.Reason = reason;
        concession.ValidFrom = request.ValidFrom;
        concession.ValidTo = request.ValidTo;
        concession.IsActive = request.IsActive;

        await auditService.LogAsync(
            "StudentConcession.Updated",
            nameof(StudentConcession),
            concession.Id.ToString(),
            new { concession.StudentId, concession.Name, DiscountType = concession.DiscountType.ToString(), concession.Value, concession.IsActive },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapConcessionsAsync([concession], cancellationToken)).Single();
    }

    public async Task DeleteConcessionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        TenantGuard.Require(tenantContext);
        var concession = await db.StudentConcessions.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(StudentConcession), id.ToString());

        db.StudentConcessions.Remove(concession);
        await auditService.LogAsync(
            "StudentConcession.Deleted",
            nameof(StudentConcession),
            concession.Id.ToString(),
            new { concession.Name, concession.StudentId },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GenerateInvoicesResultDto> GenerateMonthlyInvoicesAsync(
        GenerateMonthlyInvoicesRequest request,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? TenantGuard.Require(tenantContext);
        var today = ColomboClock.Today();
        var defaultPeriod = FeeRules.NormalizeMonthlyPeriod(request.BillingPeriod, today);
        var sem = GetTenantLock(tenantId);

        await sem.WaitAsync(cancellationToken);
        try
        {
            using (tenantContext.BeginSystemScope(tenantId))
            {
                var structuresQuery = db.FeeStructures
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => !s.IsDeleted && s.TenantId == tenantId && s.IsActive);

                if (request.FeeStructureId.HasValue)
                {
                    structuresQuery = structuresQuery.Where(s => s.Id == request.FeeStructureId.Value);
                }
                else
                {
                    structuresQuery = structuresQuery.Where(s => s.Frequency == FeeFrequency.Monthly);
                }

                if (request.BatchId.HasValue)
                {
                    var bid = request.BatchId.Value;
                    structuresQuery = structuresQuery.Where(s => s.BatchId == bid || s.BatchId == null);
                }

                if (request.EducationLevelId.HasValue)
                {
                    var lid = request.EducationLevelId.Value;
                    structuresQuery = structuresQuery.Where(s => s.EducationLevelId == lid);
                }

                var structures = await structuresQuery.OrderBy(s => s.Name).ToListAsync(cancellationToken);
                if (structures.Count == 0)
                {
                    return new GenerateInvoicesResultDto(defaultPeriod, 0, 0, 0m, []);
                }

                var activeStudents = await db.Students
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(s => !s.IsDeleted && s.TenantId == tenantId && s.Status == StudentStatus.Active)
                    .ToListAsync(cancellationToken);

                var enrollments = await db.StudentEnrollments
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(e => !e.IsDeleted && e.TenantId == tenantId && e.IsActive)
                    .ToListAsync(cancellationToken);

                var concessions = await db.StudentConcessions
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(c => !c.IsDeleted && c.TenantId == tenantId && c.IsActive)
                    .ToListAsync(cancellationToken);

                var existingInvoices = await db.FeeInvoices
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(i => !i.IsDeleted && i.TenantId == tenantId && i.Status != FeeInvoiceStatus.Voided)
                    .Select(i => new { i.StudentId, i.FeeStructureId, i.BillingPeriod })
                    .ToListAsync(cancellationToken);

                var existingKeys = existingInvoices
                    .Select(i => $"{i.StudentId:N}:{i.FeeStructureId:N}:{i.BillingPeriod}")
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var nextInvoiceSeq = await NextInvoiceSequenceUnlockedAsync(tenantId, cancellationToken);
                var createdInvoices = new List<FeeInvoice>();
                var skippedCount = 0;

                foreach (var structure in structures)
                {
                    var period = FeeRules.ResolvePeriodForStructure(structure.Frequency, request.BillingPeriod, today);
                    var dueDate = request.DueDateOverride ?? FeeRules.ComputeDueDate(period, structure.DueDayOfMonth, today);

                    IEnumerable<Student> eligibleStudents;
                    if (structure.BatchId.HasValue)
                    {
                        var targetBatchId = structure.BatchId.Value;
                        var enrolledStudentIds = enrollments
                            .Where(e => e.BatchId == targetBatchId)
                            .Select(e => e.StudentId)
                            .ToHashSet();
                        eligibleStudents = activeStudents.Where(s => enrolledStudentIds.Contains(s.Id));
                    }
                    else if (request.BatchId.HasValue)
                    {
                        var targetBatchId = request.BatchId.Value;
                        var enrolledStudentIds = enrollments
                            .Where(e => e.BatchId == targetBatchId)
                            .Select(e => e.StudentId)
                            .ToHashSet();
                        eligibleStudents = activeStudents.Where(s =>
                            enrolledStudentIds.Contains(s.Id)
                            && (!structure.EducationLevelId.HasValue || s.EducationLevelId == structure.EducationLevelId.Value));
                    }
                    else if (structure.EducationLevelId.HasValue)
                    {
                        eligibleStudents = activeStudents.Where(s => s.EducationLevelId == structure.EducationLevelId.Value);
                    }
                    else
                    {
                        eligibleStudents = activeStudents;
                    }

                    foreach (var student in eligibleStudents.OrderBy(s => s.StudentCode))
                    {
                        var key = $"{student.Id:N}:{structure.Id:N}:{period}";
                        if (!existingKeys.Add(key))
                        {
                            skippedCount++;
                            continue;
                        }

                        var concession = ResolveBestConcession(concessions, student.Id, structure.Id, structure.Amount, today);
                        var (discount, net) = FeeRules.CalculateInvoiceAmounts(
                            structure.Amount,
                            concession?.DiscountType,
                            concession?.Value);

                        var primaryBatchId = structure.BatchId
                            ?? request.BatchId
                            ?? enrollments.FirstOrDefault(e => e.StudentId == student.Id)?.BatchId;

                        var invoice = new FeeInvoice
                        {
                            TenantId = tenantId,
                            InvoiceNumber = FeeRules.FormatInvoiceNumber(nextInvoiceSeq++),
                            StudentId = student.Id,
                            FeeStructureId = structure.Id,
                            BatchId = primaryBatchId,
                            EducationLevelId = structure.EducationLevelId ?? student.EducationLevelId,
                            BillingPeriod = period,
                            Title = structure.Frequency == FeeFrequency.Monthly ? $"{structure.Name} ({period})" : structure.Name,
                            GrossAmount = FeeRules.RoundMoney(structure.Amount),
                            DiscountAmount = discount,
                            ConcessionId = concession?.Id,
                            ConcessionDescription = concession is null
                                ? null
                                : $"{concession.Name} ({(concession.DiscountType == DiscountType.Percentage ? $"{concession.Value:0.##}%" : FeeRules.FormatCurrency(concession.Value, structure.Currency))})",
                            NetAmount = net,
                            PaidAmount = 0m,
                            BalanceAmount = net,
                            Currency = structure.Currency,
                            IssueDate = today,
                            DueDate = dueDate,
                            Status = net == 0m ? FeeInvoiceStatus.Paid : FeeInvoiceStatus.Pending,
                            PaidAt = net == 0m ? DateTimeOffset.UtcNow : null
                        };

                        db.FeeInvoices.Add(invoice);
                        createdInvoices.Add(invoice);
                    }
                }

                if (createdInvoices.Count > 0)
                {
                    await auditService.LogAsync(
                        "FeeInvoices.BatchGenerated",
                        nameof(FeeInvoice),
                        null,
                        new
                        {
                            BillingPeriod = defaultPeriod,
                            CreatedCount = createdInvoices.Count,
                            SkippedExistingCount = skippedCount,
                            TotalNetAmount = createdInvoices.Sum(i => i.NetAmount)
                        },
                        tenantIdOverride: tenantId,
                        cancellationToken: cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);
                }

                var mapped = await MapInvoicesAsync(createdInvoices, tenantId, cancellationToken);
                return new GenerateInvoicesResultDto(
                    defaultPeriod,
                    createdInvoices.Count,
                    skippedCount,
                    FeeRules.RoundMoney(createdInvoices.Sum(i => i.NetAmount)),
                    mapped);
            }
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<FeeInvoiceDto> CreateSingleInvoiceAsync(
        CreateSingleInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var today = ColomboClock.Today();
        var sem = GetTenantLock(tenantId);

        await sem.WaitAsync(cancellationToken);
        try
        {
            var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Student), request.StudentId.ToString());

            var structure = await db.FeeStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.FeeStructureId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FeeStructure), request.FeeStructureId.ToString());

            var period = FeeRules.ResolvePeriodForStructure(structure.Frequency, request.BillingPeriod, today);
            var existing = await db.FeeInvoices.FirstOrDefaultAsync(
                i => i.StudentId == student.Id
                     && i.FeeStructureId == structure.Id
                     && i.BillingPeriod == period
                     && i.Status != FeeInvoiceStatus.Voided,
                cancellationToken);

            if (existing is not null)
            {
                return (await MapInvoicesAsync([existing], tenantId, cancellationToken)).Single();
            }

            var concessions = await db.StudentConcessions
                .AsNoTracking()
                .Where(c => c.StudentId == student.Id && c.IsActive)
                .ToListAsync(cancellationToken);

            var concession = ResolveBestConcession(concessions, student.Id, structure.Id, structure.Amount, today);
            var (discount, net) = FeeRules.CalculateInvoiceAmounts(
                structure.Amount,
                concession?.DiscountType,
                concession?.Value);

            var batchId = structure.BatchId
                ?? await db.StudentEnrollments
                    .AsNoTracking()
                    .Where(e => e.StudentId == student.Id && e.IsActive)
                    .Select(e => (Guid?)e.BatchId)
                    .FirstOrDefaultAsync(cancellationToken);

            var nextSeq = await NextInvoiceSequenceUnlockedAsync(tenantId, cancellationToken);
            var dueDate = request.DueDate ?? FeeRules.ComputeDueDate(period, structure.DueDayOfMonth, today);

            var invoice = new FeeInvoice
            {
                TenantId = tenantId,
                InvoiceNumber = FeeRules.FormatInvoiceNumber(nextSeq),
                StudentId = student.Id,
                FeeStructureId = structure.Id,
                BatchId = batchId,
                EducationLevelId = structure.EducationLevelId ?? student.EducationLevelId,
                BillingPeriod = period,
                Title = structure.Frequency == FeeFrequency.Monthly ? $"{structure.Name} ({period})" : structure.Name,
                GrossAmount = FeeRules.RoundMoney(structure.Amount),
                DiscountAmount = discount,
                ConcessionId = concession?.Id,
                ConcessionDescription = concession is null
                    ? null
                    : $"{concession.Name} ({(concession.DiscountType == DiscountType.Percentage ? $"{concession.Value:0.##}%" : FeeRules.FormatCurrency(concession.Value, structure.Currency))})",
                NetAmount = net,
                PaidAmount = 0m,
                BalanceAmount = net,
                Currency = structure.Currency,
                IssueDate = today,
                DueDate = dueDate,
                Status = net == 0m ? FeeInvoiceStatus.Paid : FeeInvoiceStatus.Pending,
                PaidAt = net == 0m ? DateTimeOffset.UtcNow : null,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
            };

            db.FeeInvoices.Add(invoice);
            await auditService.LogAsync(
                "FeeInvoice.Created",
                nameof(FeeInvoice),
                invoice.Id.ToString(),
                new { invoice.InvoiceNumber, invoice.StudentId, invoice.BillingPeriod, invoice.NetAmount },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return (await MapInvoicesAsync([invoice], tenantId, cancellationToken)).Single();
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<FeeInvoicePageDto> GetInvoicesAsync(
        FeeInvoiceListQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var filtered = BuildFilteredInvoicesQuery(query);
        var total = await filtered.CountAsync(cancellationToken);
        var items = await filtered
            .OrderByDescending(i => i.IssueDate)
            .ThenByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var mapped = await MapInvoicesAsync(items, tenantId, cancellationToken);
        return new FeeInvoicePageDto(mapped, total, page, pageSize);
    }

    public async Task<FeeInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var invoice = await db.FeeInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeInvoice), id.ToString());

        return (await MapInvoicesAsync([invoice], tenantId, cancellationToken)).Single();
    }

    public async Task<FeeInvoiceDto> VoidInvoiceAsync(
        Guid id,
        VoidInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var reason = NormalizeRequiredReason(request.Reason, "Enter a reason to void the invoice.");

        var invoice = await db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeInvoice), id.ToString());

        if (invoice.Status == FeeInvoiceStatus.Voided)
        {
            throw new DomainValidationException("INVOICE_ALREADY_VOIDED", "This invoice is already voided.");
        }

        if (invoice.PaidAmount > 0m)
        {
            throw new DomainValidationException(
                "INVOICE_HAS_ACTIVE_PAYMENTS",
                "Refund or void existing payments before voiding this invoice.",
                409);
        }

        invoice.Status = FeeInvoiceStatus.Voided;
        invoice.BalanceAmount = 0m;
        invoice.VoidedAt = DateTimeOffset.UtcNow;
        invoice.VoidReason = reason;
        invoice.VoidedByUserId = tenantContext.UserId;

        await auditService.LogAsync(
            "FeeInvoice.Voided",
            nameof(FeeInvoice),
            invoice.Id.ToString(),
            new { invoice.InvoiceNumber, Reason = reason },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapInvoicesAsync([invoice], tenantId, cancellationToken)).Single();
    }

    public async Task<FeePaymentDto> RecordManualPaymentAsync(
        RecordManualPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var amount = FeeRules.RoundMoney(request.Amount);
        if (amount <= 0m)
        {
            throw new DomainValidationException("INVALID_PAYMENT_AMOUNT", "Payment amount must be greater than zero.");
        }

        if (request.Method == PaymentMethod.PayHere)
        {
            throw new DomainValidationException("INVALID_PAYMENT_METHOD", "Use the PayHere checkout flow for online payments.");
        }

        var sem = GetTenantLock(tenantId);
        FeePayment payment;
        FeeInvoice invoice;

        await sem.WaitAsync(cancellationToken);
        try
        {
            invoice = await db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FeeInvoice), request.InvoiceId.ToString());

            if (invoice.Status == FeeInvoiceStatus.Voided)
            {
                throw new DomainValidationException("INVOICE_VOIDED", "Cannot record a payment against a voided invoice.");
            }

            if (invoice.Status == FeeInvoiceStatus.Paid || invoice.BalanceAmount <= 0m)
            {
                throw new DomainValidationException("INVOICE_ALREADY_PAID", "This invoice has already been paid in full.");
            }

            if (amount > invoice.BalanceAmount)
            {
                throw new DomainValidationException(
                    "OVERPAYMENT_NOT_ALLOWED",
                    $"Payment amount ({FeeRules.FormatCurrency(amount, invoice.Currency)}) exceeds the remaining invoice balance ({FeeRules.FormatCurrency(invoice.BalanceAmount, invoice.Currency)}).");
            }

            payment = await InsertPaymentWithReceiptRetryAsync(
                tenantId,
                invoice,
                amount,
                request.Method,
                request.ReferenceNumber,
                request.PaidAt ?? DateTimeOffset.UtcNow,
                request.Notes,
                cancellationToken);
        }
        finally
        {
            sem.Release();
        }

        await NotifyPaymentReceivedAsync(tenantId, invoice, payment, cancellationToken);
        return (await MapPaymentsAsync([payment], tenantId, cancellationToken)).Single();
    }

    public async Task<FeePaymentDto> UploadPaymentAttachmentAsync(
        Guid paymentId,
        Stream content,
        string fileName,
        string? contentType,
        long length,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        if (length <= 0 || length > MaxAttachmentBytes)
        {
            throw new DomainValidationException("ATTACHMENT_TOO_LARGE", "Payment slip attachments must be 5 MB or smaller.");
        }

        var extension = ResolveAttachmentExtension(fileName, contentType);
        var payment = await db.FeePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeePayment), paymentId.ToString());

        var limitCheck = await limitService.CheckAsync(tenantId, LimitKey.CloudStorageGb, increment: 0, cancellationToken: cancellationToken);
        if (limitCheck.MaxAllowed is int maxGb)
        {
            var studentBytes = await db.Students.IgnoreQueryFilters()
                .Where(s => !s.IsDeleted && s.TenantId == tenantId)
                .SumAsync(s => (long?)s.PhotoBytes, cancellationToken) ?? 0;
            var paymentBytes = await db.FeePayments.IgnoreQueryFilters()
                .Where(p => !p.IsDeleted && p.TenantId == tenantId)
                .SumAsync(p => (long?)p.AttachmentBytes, cancellationToken) ?? 0;
            var maxBytes = maxGb * 1024L * 1024L * 1024L;
            if (studentBytes + paymentBytes - payment.AttachmentBytes + length > maxBytes)
            {
                throw new PlanLimitReachedException(LimitKey.CloudStorageGb, maxGb);
            }
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var stored = await fileStorage.SaveAsync($"payments/{tenantId:N}", $"{Guid.NewGuid():N}{extension}", buffer, cancellationToken);
        payment.AttachmentUrl = stored.Url;
        payment.AttachmentBytes = stored.SizeBytes;

        await auditService.LogAsync(
            "FeePayment.AttachmentUploaded",
            nameof(FeePayment),
            payment.Id.ToString(),
            new { payment.ReceiptNumber, payment.AttachmentUrl, payment.AttachmentBytes },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return (await MapPaymentsAsync([payment], tenantId, cancellationToken)).Single();
    }

    public async Task<FeePaymentDto> RefundOrVoidPaymentAsync(
        Guid paymentId,
        RefundOrVoidPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var reason = NormalizeRequiredReason(request.Reason, "Enter a reason for the refund or void.");
        var sem = GetTenantLock(tenantId);

        await sem.WaitAsync(cancellationToken);
        try
        {
            var payment = await db.FeePayments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FeePayment), paymentId.ToString());

            if (payment.Status == PaymentRecordStatus.Voided)
            {
                throw new DomainValidationException("PAYMENT_ALREADY_VOIDED", "This payment has already been voided.");
            }

            var remainingEffective = FeeRules.EffectivePaymentAmount(payment.Amount, payment.RefundedAmount, payment.Status);
            if (remainingEffective <= 0m)
            {
                throw new DomainValidationException("PAYMENT_ALREADY_REFUNDED", "This payment has already been refunded in full.");
            }

            var invoice = await db.FeeInvoices.FirstOrDefaultAsync(i => i.Id == payment.InvoiceId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(FeeInvoice), payment.InvoiceId.ToString());

            string actionName;
            if (request.RefundAmount.HasValue)
            {
                var refund = FeeRules.RoundMoney(request.RefundAmount.Value);
                if (refund <= 0m || refund > remainingEffective)
                {
                    throw new DomainValidationException(
                        "INVALID_REFUND_AMOUNT",
                        $"Refund amount must be between 0.01 and {remainingEffective:0.00}.");
                }

                payment.RefundedAmount = FeeRules.RoundMoney(payment.RefundedAmount + refund);
                payment.Status = PaymentRecordStatus.Refunded;
                actionName = "FeePayment.Refunded";
            }
            else
            {
                payment.RefundedAmount = payment.Amount;
                payment.Status = PaymentRecordStatus.Voided;
                actionName = "FeePayment.Voided";
            }

            payment.VoidedOrRefundedAt = DateTimeOffset.UtcNow;
            payment.VoidOrRefundReason = reason;
            payment.VoidedOrRefundedByUserId = tenantContext.UserId;

            await RecalculateInvoiceSettlementAsync(invoice, cancellationToken);

            await auditService.LogAsync(
                actionName,
                nameof(FeePayment),
                payment.Id.ToString(),
                new
                {
                    payment.ReceiptNumber,
                    payment.InvoiceId,
                    payment.Amount,
                    payment.RefundedAmount,
                    Status = payment.Status.ToString(),
                    Reason = reason
                },
                cancellationToken: cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return (await MapPaymentsAsync([payment], tenantId, cancellationToken)).Single();
        }
        finally
        {
            sem.Release();
        }
    }

    public async Task<IReadOnlyList<FeePaymentDto>> GetPaymentsAsync(
        Guid? invoiceId = null,
        Guid? studentId = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var query = db.FeePayments.AsNoTracking();
        if (invoiceId.HasValue)
        {
            query = query.Where(p => p.InvoiceId == invoiceId.Value);
        }

        if (studentId.HasValue)
        {
            query = query.Where(p => p.StudentId == studentId.Value);
        }

        var payments = await query
            .OrderByDescending(p => p.PaidAt)
            .ThenByDescending(p => p.ReceiptSequence)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);

        return await MapPaymentsAsync(payments, tenantId, cancellationToken);
    }

    public async Task<StoredDownload> GetReceiptPdfAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var payment = await db.FeePayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeePayment), paymentId.ToString());

        return await BuildReceiptDownloadAsync(payment, tenantId, cancellationToken);
    }

    public async Task<RunFeeRemindersResultDto> ProcessFeeRemindersAsync(
        DateOnly? dateOverride = null,
        Guid? tenantIdOverride = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantIdOverride ?? TenantGuard.Require(tenantContext);
        var today = dateOverride ?? ColomboClock.Today();

        using (tenantContext.BeginSystemScope(tenantId))
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(Tenant), tenantId.ToString());

            var config = await db.TenantNotificationConfigs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId, cancellationToken);

            var daysBefore = config?.FeeReminderDaysBefore ?? 3;
            var onDueDate = config?.FeeReminderOnDueDate ?? true;
            var daysAfterOverdue = config?.FeeReminderDaysAfterOverdue ?? 3;
            var language = config?.DefaultLanguage ?? "en";

            var invoices = await db.FeeInvoices
                .IgnoreQueryFilters()
                .Where(i => !i.IsDeleted
                            && i.TenantId == tenantId
                            && (i.Status == FeeInvoiceStatus.Pending || i.Status == FeeInvoiceStatus.PartiallyPaid)
                            && i.BalanceAmount > 0m)
                .OrderBy(i => i.DueDate)
                .ToListAsync(cancellationToken);

            if (invoices.Count == 0)
            {
                return new RunFeeRemindersResultDto(today, 0, 0, 0, 0, 0);
            }

            var studentIds = invoices.Select(i => i.StudentId).Distinct().ToList();
            var students = await db.Students
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => !s.IsDeleted && s.TenantId == tenantId && studentIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, cancellationToken);

            var batchIds = invoices.Where(i => i.BatchId.HasValue).Select(i => i.BatchId!.Value).Distinct().ToList();
            var batches = await db.AcademicBatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => !b.IsDeleted && b.TenantId == tenantId && batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, cancellationToken);

            var beforeDueSent = 0;
            var onDueSent = 0;
            var overdueSent = 0;
            var skippedCount = 0;
            var now = DateTimeOffset.UtcNow;

            foreach (var invoice in invoices)
            {
                var stage = FeeRules.EvaluateReminderStage(
                    today,
                    invoice.DueDate,
                    daysBefore,
                    onDueDate,
                    daysAfterOverdue,
                    invoice.BeforeDueReminderSentAt.HasValue,
                    invoice.OnDueReminderSentAt.HasValue,
                    invoice.OverdueReminderSentAt.HasValue);

                if (!stage.HasValue)
                {
                    continue;
                }

                if (!students.TryGetValue(invoice.StudentId, out var student)
                    || string.IsNullOrWhiteSpace(student.GuardianPhone))
                {
                    skippedCount++;
                    continue;
                }

                var className = invoice.BatchId.HasValue && batches.TryGetValue(invoice.BatchId.Value, out var batch)
                    ? batch.Name
                    : invoice.Title;

                var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["studentName"] = student.FullName,
                    ["studentCode"] = student.StudentCode,
                    ["className"] = className,
                    ["batchName"] = className,
                    ["amount"] = FeeRules.FormatCurrency(invoice.BalanceAmount, invoice.Currency),
                    ["dueDate"] = invoice.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["instituteName"] = tenant.Name
                };

                var enqueueResult = await notificationService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        MessageType: NotificationMessageType.FeeDueReminder,
                        RecipientPhone: student.GuardianPhone,
                        RecipientName: student.GuardianName,
                        StudentId: student.Id,
                        StudentName: student.FullName,
                        StudentCode: student.StudentCode,
                        Language: language,
                        IdempotencyKey: $"fee-reminder:{invoice.Id:N}:{stage.Value}",
                        Variables: variables),
                    tenantIdOverride: tenantId,
                    cancellationToken: cancellationToken);

                if (enqueueResult.EnqueuedCount > 0 || enqueueResult.WasDuplicate)
                {
                    switch (stage.Value)
                    {
                        case FeeReminderStage.BeforeDue:
                            invoice.BeforeDueReminderSentAt = now;
                            beforeDueSent++;
                            break;
                        case FeeReminderStage.OnDueDate:
                            invoice.OnDueReminderSentAt = now;
                            onDueSent++;
                            break;
                        case FeeReminderStage.Overdue:
                            invoice.OverdueReminderSentAt = now;
                            overdueSent++;
                            break;
                    }
                }
                else
                {
                    skippedCount++;
                }
            }

            if (beforeDueSent + onDueSent + overdueSent > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
                await notificationDispatcher.DispatchPendingAsync(tenantIdFilter: tenantId, cancellationToken: cancellationToken);
            }

            return new RunFeeRemindersResultDto(
                EvaluatedDate: today,
                CheckedInvoices: invoices.Count,
                BeforeDueSent: beforeDueSent,
                OnDueSent: onDueSent,
                OverdueSent: overdueSent,
                SkippedCount: skippedCount);
        }
    }

    public async Task<PayHereGatewaySettingsDto> GetPayHereSettingsAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.OnlinePayments, cancellationToken);
        var config = await db.TenantPaymentGatewayConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        string? maskedSecret = null;
        var hasSecret = false;
        if (config is not null && !string.IsNullOrWhiteSpace(config.EncryptedMerchantSecret))
        {
            hasSecret = true;
            try
            {
                var plain = paymentGateway.UnprotectSecret(config.EncryptedMerchantSecret);
                maskedSecret = paymentGateway.MaskSecret(plain);
            }
            catch
            {
                maskedSecret = "••••••••";
            }
        }

        return new PayHereGatewaySettingsDto(
            MerchantId: config?.MerchantId ?? string.Empty,
            HasMerchantSecret: hasSecret,
            MaskedMerchantSecret: maskedSecret,
            IsSandbox: config?.IsSandbox ?? true,
            IsEnabled: config?.IsEnabled ?? true,
            NotifyUrlOverride: config?.NotifyUrlOverride,
            ReturnUrlOverride: config?.ReturnUrlOverride,
            CancelUrlOverride: config?.CancelUrlOverride,
            IsOnlinePaymentsFeatureEnabled: eval.IsEnabled,
            IsOnlinePaymentsInPlan: eval.IsInPlan);
    }

    public async Task<PayHereGatewaySettingsDto> UpdatePayHereSettingsAsync(
        UpdatePayHereGatewaySettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await EnsureOnlinePaymentsEnabledAsync(tenantId, cancellationToken);

        var merchantId = request.MerchantId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(merchantId))
        {
            throw new DomainValidationException("MERCHANT_ID_REQUIRED", "PayHere Merchant ID is required.");
        }

        var config = await db.TenantPaymentGatewayConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, cancellationToken);

        if (config is null)
        {
            if (string.IsNullOrWhiteSpace(request.MerchantSecret))
            {
                throw new DomainValidationException("MERCHANT_SECRET_REQUIRED", "PayHere Merchant Secret is required when configuring the gateway.");
            }

            config = new TenantPaymentGatewayConfig
            {
                TenantId = tenantId,
                MerchantId = merchantId,
                EncryptedMerchantSecret = paymentGateway.ProtectSecret(request.MerchantSecret.Trim()),
                IsSandbox = request.IsSandbox,
                IsEnabled = request.IsEnabled,
                NotifyUrlOverride = NullIfBlank(request.NotifyUrlOverride),
                ReturnUrlOverride = NullIfBlank(request.ReturnUrlOverride),
                CancelUrlOverride = NullIfBlank(request.CancelUrlOverride)
            };
            db.TenantPaymentGatewayConfigs.Add(config);
        }
        else
        {
            config.MerchantId = merchantId;
            if (!string.IsNullOrWhiteSpace(request.MerchantSecret))
            {
                config.EncryptedMerchantSecret = paymentGateway.ProtectSecret(request.MerchantSecret.Trim());
            }

            config.IsSandbox = request.IsSandbox;
            config.IsEnabled = request.IsEnabled;
            config.NotifyUrlOverride = NullIfBlank(request.NotifyUrlOverride);
            config.ReturnUrlOverride = NullIfBlank(request.ReturnUrlOverride);
            config.CancelUrlOverride = NullIfBlank(request.CancelUrlOverride);
        }

        await auditService.LogAsync(
            "PayHereSettings.Updated",
            nameof(TenantPaymentGatewayConfig),
            config.Id.ToString(),
            new { config.MerchantId, config.IsSandbox, config.IsEnabled },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await GetPayHereSettingsAsync(cancellationToken);
    }

    public async Task<PayHereCheckoutSessionDto> InitiatePayHereCheckoutAsync(
        InitiatePayHereCheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        await EnsureOnlinePaymentsEnabledAsync(tenantId, cancellationToken);

        var config = await db.TenantPaymentGatewayConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.IsEnabled, cancellationToken);

        if (config is null || string.IsNullOrWhiteSpace(config.MerchantId) || string.IsNullOrWhiteSpace(config.EncryptedMerchantSecret))
        {
            throw new DomainValidationException(
                "PAYHERE_NOT_CONFIGURED",
                "PayHere merchant credentials are not configured or enabled for this institute.");
        }

        var invoice = await db.FeeInvoices
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == request.InvoiceId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeeInvoice), request.InvoiceId.ToString());

        if (invoice.Status == FeeInvoiceStatus.Voided)
        {
            throw new DomainValidationException("INVOICE_VOIDED", "Cannot pay a voided invoice.");
        }

        if (invoice.Status == FeeInvoiceStatus.Paid || invoice.BalanceAmount <= 0m)
        {
            throw new DomainValidationException("INVOICE_ALREADY_PAID", "This invoice is already paid in full.");
        }

        var amount = request.Amount.HasValue
            ? FeeRules.RoundMoney(request.Amount.Value)
            : invoice.BalanceAmount;

        if (amount <= 0m || amount > invoice.BalanceAmount)
        {
            throw new DomainValidationException(
                "INVALID_PAYMENT_AMOUNT",
                $"Checkout amount must be between 0.01 and {invoice.BalanceAmount:0.00}.");
        }

        var student = await db.Students
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == invoice.StudentId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Student), invoice.StudentId.ToString());

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(t => t.Id == tenantId, cancellationToken);

        var merchantSecret = paymentGateway.UnprotectSecret(config.EncryptedMerchantSecret);
        var orderId = $"PH-{invoice.InvoiceNumber.Replace("-", "")}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

        var session = new PayHereCheckoutSession
        {
            TenantId = tenantId,
            OrderId = orderId,
            InvoiceId = invoice.Id,
            StudentId = student.Id,
            Amount = amount,
            Currency = invoice.Currency,
            IsSandbox = config.IsSandbox,
            Status = PayHereCheckoutStatus.Initiated
        };

        db.PayHereCheckoutSessions.Add(session);
        await auditService.LogAsync(
            "PayHere.CheckoutInitiated",
            nameof(PayHereCheckoutSession),
            session.Id.ToString(),
            new { session.OrderId, session.InvoiceId, session.Amount, session.Currency, session.IsSandbox },
            cancellationToken: cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return BuildCheckoutSessionDto(session, invoice, student, tenant, config, merchantSecret, request.ReturnUrl, request.CancelUrl, receiptNumber: null);
    }

    public async Task<PayHereCheckoutSessionDto> GetPayHereOrderStatusAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedOrderId))
        {
            throw new DomainValidationException("ORDER_ID_REQUIRED", "Order ID is required.");
        }

        var session = await db.PayHereCheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => !s.IsDeleted
                     && s.OrderId == normalizedOrderId
                     && (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty || s.TenantId == tenantContext.TenantId.Value),
                cancellationToken)
            ?? throw new EntityNotFoundException("PayHereOrder", normalizedOrderId);

        var tenantId = session.TenantId;
        var invoice = await db.FeeInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(i => i.TenantId == tenantId && i.Id == session.InvoiceId, cancellationToken);

        var student = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(s => s.TenantId == tenantId && s.Id == session.StudentId, cancellationToken);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(t => t.Id == tenantId, cancellationToken);

        var config = await db.TenantPaymentGatewayConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId, cancellationToken);

        string? receiptNumber = null;
        if (session.FeePaymentId.HasValue)
        {
            receiptNumber = await db.FeePayments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId && p.Id == session.FeePaymentId.Value)
                .Select(p => p.ReceiptNumber)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var merchantSecret = config is not null && !string.IsNullOrWhiteSpace(config.EncryptedMerchantSecret)
            ? paymentGateway.UnprotectSecret(config.EncryptedMerchantSecret)
            : string.Empty;

        return BuildCheckoutSessionDto(session, invoice, student, tenant, config, merchantSecret, null, null, receiptNumber);
    }

    public async Task<StoredDownload> GetPayHereOrderReceiptPdfAsync(
        string orderId,
        CancellationToken cancellationToken = default)
    {
        var normalizedOrderId = orderId?.Trim() ?? string.Empty;
        var session = await db.PayHereCheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => !s.IsDeleted
                     && s.OrderId == normalizedOrderId
                     && (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty || s.TenantId == tenantContext.TenantId.Value),
                cancellationToken)
            ?? throw new EntityNotFoundException("PayHereOrder", normalizedOrderId);

        if (!session.FeePaymentId.HasValue)
        {
            throw new DomainValidationException("RECEIPT_NOT_READY", "Payment has not been completed for this order yet.");
        }

        var payment = await db.FeePayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => !p.IsDeleted && p.TenantId == session.TenantId && p.Id == session.FeePaymentId.Value, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(FeePayment), session.FeePaymentId.Value.ToString());

        return await BuildReceiptDownloadAsync(payment, session.TenantId, cancellationToken);
    }

    public async Task<PayHereWebhookResultDto> ProcessPayHereWebhookAsync(
        PayHereWebhookPayload payload,
        CancellationToken cancellationToken = default)
    {
        var orderId = payload.OrderId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new DomainValidationException("ORDER_ID_REQUIRED", "order_id is required.");
        }

        var sessionLookup = await db.PayHereCheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => !s.IsDeleted && s.OrderId == orderId, cancellationToken)
            ?? throw new DomainValidationException("INVALID_ORDER", $"Order '{orderId}' was not found.");

        var tenantId = sessionLookup.TenantId;
        await EnsureOnlinePaymentsEnabledAsync(tenantId, cancellationToken);

        var config = await db.TenantPaymentGatewayConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == tenantId && c.IsEnabled, cancellationToken)
            ?? throw new DomainValidationException("PAYHERE_NOT_CONFIGURED", "PayHere gateway is not configured for this institute.");

        if (!string.Equals(config.MerchantId, payload.MerchantId?.Trim(), StringComparison.Ordinal))
        {
            throw new DomainValidationException("INVALID_MERCHANT", "PayHere merchant_id does not match the institute configuration.");
        }

        var merchantSecret = paymentGateway.UnprotectSecret(config.EncryptedMerchantSecret);
        var signatureValid = paymentGateway.VerifyWebhookSignature(
            payload.MerchantId?.Trim() ?? string.Empty,
            merchantSecret,
            orderId,
            payload.PayHereAmount?.Trim() ?? string.Empty,
            payload.PayHereCurrency?.Trim() ?? string.Empty,
            payload.StatusCode,
            payload.Md5Sig?.Trim() ?? string.Empty);

        if (!signatureValid)
        {
            using (tenantContext.BeginSystemScope(tenantId))
            {
                await auditService.LogAsync(
                    "PayHere.WebhookInvalidSignature",
                    nameof(PayHereCheckoutSession),
                    sessionLookup.Id.ToString(),
                    new { orderId, payload.MerchantId, payload.StatusCode },
                    tenantIdOverride: tenantId,
                    cancellationToken: cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }

            throw new DomainValidationException("INVALID_WEBHOOK_SIGNATURE", "PayHere md5sig verification failed.");
        }

        if (!decimal.TryParse(payload.PayHereAmount?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var webhookAmount))
        {
            throw new DomainValidationException("INVALID_PAYMENT_AMOUNT", "payhere_amount is not a valid decimal number.");
        }

        webhookAmount = FeeRules.RoundMoney(webhookAmount);
        if (webhookAmount != sessionLookup.Amount
            || !string.Equals(payload.PayHereCurrency?.Trim(), sessionLookup.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException(
                "INVALID_PAYMENT_AMOUNT",
                "Webhook amount or currency does not match the checkout session.");
        }

        var sem = GetTenantLock(tenantId);
        FeePayment? createdPayment = null;
        FeeInvoice? updatedInvoice = null;
        PayHereWebhookResultDto result;

        await sem.WaitAsync(cancellationToken);
        try
        {
            using (tenantContext.BeginSystemScope(tenantId))
            {
                var session = await db.PayHereCheckoutSessions
                    .IgnoreQueryFilters()
                    .FirstAsync(s => !s.IsDeleted && s.TenantId == tenantId && s.Id == sessionLookup.Id, cancellationToken);

                var invoice = await db.FeeInvoices
                    .IgnoreQueryFilters()
                    .FirstAsync(i => !i.IsDeleted && i.TenantId == tenantId && i.Id == session.InvoiceId, cancellationToken);

                if (session.Status == PayHereCheckoutStatus.Success && session.FeePaymentId.HasValue)
                {
                    var existingPayment = await db.FeePayments
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == session.FeePaymentId.Value, cancellationToken);

                    return new PayHereWebhookResultDto(
                        OrderId: session.OrderId,
                        Outcome: "AlreadyProcessed",
                        WasReplay: true,
                        Status: session.Status.ToString(),
                        InvoiceId: invoice.Id,
                        InvoiceStatus: invoice.Status.ToString(),
                        PaymentId: existingPayment?.Id,
                        ReceiptNumber: existingPayment?.ReceiptNumber);
                }

                if (!string.IsNullOrWhiteSpace(payload.PaymentId))
                {
                    var duplicateByRef = await db.FeePayments
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(
                            p => !p.IsDeleted
                                 && p.TenantId == tenantId
                                 && p.Method == PaymentMethod.PayHere
                                 && p.ReferenceNumber == payload.PaymentId.Trim(),
                            cancellationToken);

                    if (duplicateByRef is not null)
                    {
                        return new PayHereWebhookResultDto(
                            OrderId: session.OrderId,
                            Outcome: "AlreadyProcessed",
                            WasReplay: true,
                            Status: PayHereCheckoutStatus.Success.ToString(),
                            InvoiceId: invoice.Id,
                            InvoiceStatus: invoice.Status.ToString(),
                            PaymentId: duplicateByRef.Id,
                            ReceiptNumber: duplicateByRef.ReceiptNumber);
                    }
                }

                var now = DateTimeOffset.UtcNow;
                session.StatusCode = payload.StatusCode;
                session.StatusMessage = NullIfBlank(payload.StatusMessage);
                session.PayHerePaymentId = NullIfBlank(payload.PaymentId);
                session.LastWebhookAt = now;

                if (payload.StatusCode == 2)
                {
                    if (invoice.Status == FeeInvoiceStatus.Voided)
                    {
                        throw new DomainValidationException("INVOICE_VOIDED", "Cannot complete online payment for a voided invoice.");
                    }

                    if (webhookAmount > invoice.BalanceAmount)
                    {
                        throw new DomainValidationException("OVERPAYMENT_NOT_ALLOWED", "Payment amount exceeds the remaining invoice balance.");
                    }

                    createdPayment = await InsertPaymentWithReceiptRetryAsync(
                        tenantId,
                        invoice,
                        webhookAmount,
                        PaymentMethod.PayHere,
                        payload.PaymentId?.Trim() ?? session.OrderId,
                        now,
                        $"PayHere online payment ({session.OrderId})",
                        cancellationToken);

                    updatedInvoice = invoice;
                    session.Status = PayHereCheckoutStatus.Success;
                    session.FeePaymentId = createdPayment.Id;
                    session.CompletedAt = now;

                    await auditService.LogAsync(
                        "PayHere.WebhookPaymentCompleted",
                        nameof(PayHereCheckoutSession),
                        session.Id.ToString(),
                        new
                        {
                            session.OrderId,
                            session.PayHerePaymentId,
                            createdPayment.ReceiptNumber,
                            webhookAmount,
                            InvoiceStatus = invoice.Status.ToString()
                        },
                        tenantIdOverride: tenantId,
                        cancellationToken: cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);

                    result = new PayHereWebhookResultDto(
                        OrderId: session.OrderId,
                        Outcome: "Processed",
                        WasReplay: false,
                        Status: session.Status.ToString(),
                        InvoiceId: invoice.Id,
                        InvoiceStatus: invoice.Status.ToString(),
                        PaymentId: createdPayment.Id,
                        ReceiptNumber: createdPayment.ReceiptNumber);
                }
                else
                {
                    session.Status = payload.StatusCode switch
                    {
                        0 => PayHereCheckoutStatus.Pending,
                        -1 => PayHereCheckoutStatus.Cancelled,
                        -3 => PayHereCheckoutStatus.ChargedBack,
                        _ => PayHereCheckoutStatus.Failed
                    };

                    await auditService.LogAsync(
                        "PayHere.WebhookNonSuccess",
                        nameof(PayHereCheckoutSession),
                        session.Id.ToString(),
                        new { session.OrderId, payload.StatusCode, Status = session.Status.ToString() },
                        tenantIdOverride: tenantId,
                        cancellationToken: cancellationToken);

                    await db.SaveChangesAsync(cancellationToken);

                    return new PayHereWebhookResultDto(
                        OrderId: session.OrderId,
                        Outcome: "UpdatedStatus",
                        WasReplay: false,
                        Status: session.Status.ToString(),
                        InvoiceId: invoice.Id,
                        InvoiceStatus: invoice.Status.ToString(),
                        PaymentId: null,
                        ReceiptNumber: null);
                }
            }
        }
        finally
        {
            sem.Release();
        }

        if (createdPayment is not null && updatedInvoice is not null)
        {
            await NotifyPaymentReceivedAsync(tenantId, updatedInvoice, createdPayment, cancellationToken);
        }

        return result;
    }

    public async Task<PayHereWebhookResultDto> SimulatePayHereSandboxAsync(
        SimulatePayHereSandboxRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderId = request.OrderId?.Trim() ?? string.Empty;
        var session = await db.PayHereCheckoutSessions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                s => !s.IsDeleted
                     && s.OrderId == orderId
                     && (!tenantContext.TenantId.HasValue || tenantContext.TenantId.Value == Guid.Empty || s.TenantId == tenantContext.TenantId.Value),
                cancellationToken)
            ?? throw new EntityNotFoundException("PayHereOrder", orderId);

        if (!session.IsSandbox)
        {
            throw new DomainValidationException("SANDBOX_ONLY", "Sandbox completion can only be used when the gateway is in sandbox mode.");
        }

        await EnsureOnlinePaymentsEnabledAsync(session.TenantId, cancellationToken);

        var config = await db.TenantPaymentGatewayConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.TenantId == session.TenantId && c.IsEnabled, cancellationToken)
            ?? throw new DomainValidationException("PAYHERE_NOT_CONFIGURED", "PayHere gateway is not configured for this institute.");

        var merchantSecret = paymentGateway.UnprotectSecret(config.EncryptedMerchantSecret);
        var formattedAmount = paymentGateway.FormatAmount(session.Amount);
        var md5sig = paymentGateway.GenerateWebhookSignature(
            config.MerchantId,
            merchantSecret,
            session.OrderId,
            formattedAmount,
            session.Currency,
            request.StatusCode);

        var paymentId = string.IsNullOrWhiteSpace(request.PaymentId)
            ? $"SBX-{session.Id.ToString("N")[..10].ToUpperInvariant()}"
            : request.PaymentId.Trim();

        return await ProcessPayHereWebhookAsync(
            new PayHereWebhookPayload(
                MerchantId: config.MerchantId,
                OrderId: session.OrderId,
                PaymentId: paymentId,
                PayHereAmount: formattedAmount,
                PayHereCurrency: session.Currency,
                StatusCode: request.StatusCode,
                Md5Sig: md5sig,
                Method: "VISA",
                StatusMessage: request.StatusCode == 2 ? "Sandbox payment approved" : "Sandbox payment status updated"),
            cancellationToken);
    }

    public async Task<FeeDashboardDto> GetDashboardAsync(
        FeeDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var today = query.AsOfDate ?? ColomboClock.Today();

        var invoicesQuery = db.FeeInvoices
            .AsNoTracking()
            .Where(i => i.Status != FeeInvoiceStatus.Voided);

        if (!string.IsNullOrWhiteSpace(query.BillingPeriod))
        {
            var period = query.BillingPeriod.Trim();
            invoicesQuery = invoicesQuery.Where(i => i.BillingPeriod == period);
        }

        if (query.BatchId.HasValue)
        {
            invoicesQuery = invoicesQuery.Where(i => i.BatchId == query.BatchId.Value);
        }

        var invoices = await invoicesQuery.ToListAsync(cancellationToken);

        var expectedTotal = FeeRules.RoundMoney(invoices.Sum(i => i.NetAmount));
        var collectedTotal = FeeRules.RoundMoney(invoices.Sum(i => i.PaidAmount));
        var outstandingTotal = FeeRules.RoundMoney(invoices.Sum(i => i.BalanceAmount));

        var overdueInvoices = invoices
            .Where(i => i.BalanceAmount > 0m && i.DueDate < today)
            .ToList();

        var overdueTotal = FeeRules.RoundMoney(overdueInvoices.Sum(i => i.BalanceAmount));
        var collectionRate = expectedTotal > 0m
            ? Math.Round(collectedTotal * 100m / expectedTotal, 1)
            : 0m;

        var agingBuckets = FeeRules.AgingBuckets
            .Select(bucket =>
            {
                var matching = invoices
                    .Where(i => i.BalanceAmount > 0m && FeeRules.ClassifyAgingBucket(today, i.DueDate) == bucket)
                    .ToList();
                return new OverdueAgingBucketDto(
                    bucket,
                    matching.Count,
                    FeeRules.RoundMoney(matching.Sum(i => i.BalanceAmount)));
            })
            .ToList();

        var batches = await db.AcademicBatches
            .AsNoTracking()
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var byBatch = invoices
            .GroupBy(i => i.BatchId)
            .Select(g =>
            {
                var expected = FeeRules.RoundMoney(g.Sum(x => x.NetAmount));
                var collected = FeeRules.RoundMoney(g.Sum(x => x.PaidAmount));
                var outstanding = FeeRules.RoundMoney(g.Sum(x => x.BalanceAmount));
                var rate = expected > 0m ? Math.Round(collected * 100m / expected, 1) : 0m;
                var name = g.Key.HasValue && batches.TryGetValue(g.Key.Value, out var bName)
                    ? bName
                    : "General / Unassigned";
                return new BatchFeeSummaryDto(
                    g.Key,
                    name,
                    g.Count(),
                    expected,
                    collected,
                    outstanding,
                    rate);
            })
            .OrderByDescending(b => b.ExpectedAmount)
            .ToList();

        var defaulterStudentIds = overdueInvoices.Select(i => i.StudentId).Distinct().ToList();
        var students = await db.Students
            .AsNoTracking()
            .Where(s => defaulterStudentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var enrollments = await db.StudentEnrollments
            .AsNoTracking()
            .Where(e => defaulterStudentIds.Contains(e.StudentId) && e.IsActive)
            .ToListAsync(cancellationToken);

        var topDefaulters = overdueInvoices
            .GroupBy(i => i.StudentId)
            .Select(g =>
            {
                students.TryGetValue(g.Key, out var student);
                var studentBatchNames = enrollments
                    .Where(e => e.StudentId == g.Key)
                    .Select(e => batches.TryGetValue(e.BatchId, out var bn) ? bn : null)
                    .Where(bn => bn is not null)
                    .Distinct()
                    .ToList();
                var oldestDue = g.Min(i => i.DueDate);
                var maxDays = Math.Max(0, today.DayNumber - oldestDue.DayNumber);
                return new DefaulterStudentDto(
                    g.Key,
                    student?.StudentCode ?? string.Empty,
                    student?.FullName ?? "Student",
                    student?.GuardianName ?? string.Empty,
                    student?.GuardianPhone ?? string.Empty,
                    studentBatchNames.Count > 0 ? string.Join(", ", studentBatchNames) : "-",
                    g.Count(),
                    FeeRules.RoundMoney(g.Sum(i => i.BalanceAmount)),
                    oldestDue,
                    maxDays);
            })
            .OrderByDescending(d => d.TotalOverdueBalance)
            .ThenByDescending(d => d.MaxDaysOverdue)
            .Take(10)
            .ToList();

        var recentPayments = await GetPaymentsAsync(take: 10, cancellationToken: cancellationToken);

        return new FeeDashboardDto(
            ExpectedTotal: expectedTotal,
            CollectedTotal: collectedTotal,
            OutstandingTotal: outstandingTotal,
            OverdueTotal: overdueTotal,
            CollectionRatePercent: collectionRate,
            PaidInvoiceCount: invoices.Count(i => i.Status == FeeInvoiceStatus.Paid),
            PartiallyPaidInvoiceCount: invoices.Count(i => i.Status == FeeInvoiceStatus.PartiallyPaid),
            PendingInvoiceCount: invoices.Count(i => i.Status == FeeInvoiceStatus.Pending),
            OverdueInvoiceCount: overdueInvoices.Count,
            OverdueAging: agingBuckets,
            ByBatch: byBatch,
            TopDefaulters: topDefaulters,
            RecentPayments: recentPayments);
    }

    public async Task<StoredDownload> ExportFeeReportAsync(
        string? format,
        FeeInvoiceListQuery query,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantGuard.Require(tenantContext);
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.DataExport, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.DataExport);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.DataExport);
        }

        var invoices = await BuildFilteredInvoicesQuery(query)
            .OrderByDescending(i => i.IssueDate)
            .ThenBy(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var mapped = await MapInvoicesAsync(invoices, tenantId, cancellationToken);
        var headers = new[]
        {
            "invoiceNumber",
            "studentCode",
            "studentName",
            "batch",
            "billingPeriod",
            "netAmount",
            "paidAmount",
            "balanceAmount",
            "dueDate",
            "status"
        };

        var rows = mapped.Select(i => (IReadOnlyList<string>)
        [
            i.InvoiceNumber,
            i.StudentCode,
            i.StudentName,
            i.BatchName ?? "-",
            i.BillingPeriod,
            i.NetAmount.ToString("0.00", CultureInfo.InvariantCulture),
            i.PaidAmount.ToString("0.00", CultureInfo.InvariantCulture),
            i.BalanceAmount.ToString("0.00", CultureInfo.InvariantCulture),
            i.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            i.Status
        ]).ToList();

        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return new StoredDownload(
                workbook.Write("FeeInvoices", headers, rows),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "fee-invoices.xlsx");
        }

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var branding = await LoadBrandingAsync(tenantId, cancellationToken);
            var pdfRows = mapped.Select(i => new FeeReportExportRow(
                i.InvoiceNumber,
                i.StudentCode,
                i.StudentName,
                i.BatchName ?? "-",
                i.BillingPeriod,
                i.NetAmount,
                i.PaidAmount,
                i.BalanceAmount,
                i.DueDate,
                i.Status)).ToList();

            return new StoredDownload(
                feeDocuments.BuildFeeReportPdf(branding.Name, branding.PrimaryHex, pdfRows),
                "application/pdf",
                "fee-invoices.pdf");
        }

        return new StoredDownload(
            CsvTable.Write(headers, rows),
            "text/csv",
            "fee-invoices.csv");
    }

    private async Task<FeePayment> InsertPaymentWithReceiptRetryAsync(
        Guid tenantId,
        FeeInvoice invoice,
        decimal amount,
        PaymentMethod method,
        string? referenceNumber,
        DateTimeOffset paidAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var nextSeq = await NextReceiptSequenceUnlockedAsync(tenantId, cancellationToken);
            var payment = new FeePayment
            {
                TenantId = tenantId,
                InvoiceId = invoice.Id,
                StudentId = invoice.StudentId,
                ReceiptSequence = nextSeq,
                ReceiptNumber = FeeRules.FormatReceiptNumber(nextSeq),
                Amount = amount,
                RefundedAmount = 0m,
                Currency = invoice.Currency,
                Method = method,
                Status = PaymentRecordStatus.Completed,
                ReferenceNumber = NullIfBlank(referenceNumber),
                PaidAt = paidAt,
                RecordedByUserId = tenantContext.UserId,
                Notes = NullIfBlank(notes)
            };

            db.FeePayments.Add(payment);
            await RecalculateInvoiceSettlementAsync(invoice, cancellationToken, pendingNewPayment: payment);

            await auditService.LogAsync(
                "FeePayment.Recorded",
                nameof(FeePayment),
                payment.Id.ToString(),
                new
                {
                    payment.ReceiptNumber,
                    payment.InvoiceId,
                    invoice.InvoiceNumber,
                    payment.Amount,
                    Method = payment.Method.ToString(),
                    payment.ReferenceNumber
                },
                tenantIdOverride: tenantId,
                cancellationToken: cancellationToken);

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return payment;
            }
            catch (DbUpdateException) when (attempt < 4)
            {
                if (db is DbContext efContext)
                {
                    efContext.Entry(payment).State = EntityState.Detached;
                }
            }
        }

        throw new DomainValidationException("RECEIPT_SEQUENCE_CONFLICT", "Could not allocate a unique receipt number. Please retry.");
    }

    private async Task RecalculateInvoiceSettlementAsync(
        FeeInvoice invoice,
        CancellationToken cancellationToken,
        FeePayment? pendingNewPayment = null)
    {
        var existingPayments = await db.FeePayments
            .IgnoreQueryFilters()
            .Where(p => !p.IsDeleted && p.TenantId == invoice.TenantId && p.InvoiceId == invoice.Id)
            .ToListAsync(cancellationToken);

        if (pendingNewPayment is not null && existingPayments.All(p => p.Id != pendingNewPayment.Id))
        {
            existingPayments.Add(pendingNewPayment);
        }

        var (paid, balance, status) = FeeRules.ComputeInvoiceSettlement(
            invoice.NetAmount,
            existingPayments.Select(p => (p.Amount, p.RefundedAmount, p.Status)),
            invoice.Status == FeeInvoiceStatus.Voided);

        invoice.PaidAmount = paid;
        invoice.BalanceAmount = balance;
        invoice.Status = status;
        invoice.PaidAt = status == FeeInvoiceStatus.Paid
            ? invoice.PaidAt ?? DateTimeOffset.UtcNow
            : null;
    }

    private async Task NotifyPaymentReceivedAsync(
        Guid tenantId,
        FeeInvoice invoice,
        FeePayment payment,
        CancellationToken cancellationToken)
    {
        using (tenantContext.BeginSystemScope(tenantId))
        {
            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(t => !t.IsDeleted && t.Id == tenantId, cancellationToken);

            var student = await db.Students
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(s => !s.IsDeleted && s.TenantId == tenantId && s.Id == payment.StudentId, cancellationToken);

            var trackedPayment = await db.FeePayments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Id == payment.Id, cancellationToken);

            if (tenant is null || student is null || trackedPayment is null)
            {
                return;
            }

            var formattedAmount = FeeRules.FormatCurrency(payment.Amount, payment.Currency);
            var dateStr = ColomboClock.Today().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["studentName"] = student.FullName,
                ["studentCode"] = student.StudentCode,
                ["className"] = invoice.Title,
                ["amount"] = formattedAmount,
                ["receiptNo"] = payment.ReceiptNumber,
                ["date"] = dateStr,
                ["instituteName"] = tenant.Name
            };

            if (!trackedPayment.ParentNotified && !string.IsNullOrWhiteSpace(student.GuardianPhone))
            {
                var parentRes = await notificationService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        MessageType: NotificationMessageType.PaymentReceivedParent,
                        RecipientPhone: student.GuardianPhone,
                        RecipientName: student.GuardianName,
                        StudentId: student.Id,
                        StudentName: student.FullName,
                        StudentCode: student.StudentCode,
                        IdempotencyKey: $"payment-parent:{payment.Id:N}",
                        Variables: variables),
                    tenantIdOverride: tenantId,
                    cancellationToken: cancellationToken);

                if (parentRes.EnqueuedCount > 0 || parentRes.WasDuplicate)
                {
                    trackedPayment.ParentNotified = true;
                }
            }

            if (!trackedPayment.AdminNotified)
            {
                var adminRes = await notificationService.EnqueueAsync(
                    new EnqueueNotificationRequest(
                        MessageType: NotificationMessageType.PaymentReceivedAdmin,
                        RecipientPhone: tenant.ContactPhone ?? "+94771234567",
                        RecipientEmail: tenant.ContactEmail ?? "admin@demo.eduflow.lk",
                        RecipientName: tenant.Name,
                        StudentId: student.Id,
                        StudentName: student.FullName,
                        StudentCode: student.StudentCode,
                        IdempotencyKey: $"payment-admin:{payment.Id:N}",
                        Variables: variables),
                    tenantIdOverride: tenantId,
                    cancellationToken: cancellationToken);

                if (adminRes.EnqueuedCount > 0 || adminRes.WasDuplicate)
                {
                    trackedPayment.AdminNotified = true;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await notificationDispatcher.DispatchPendingAsync(tenantIdFilter: tenantId, cancellationToken: cancellationToken);
        }
    }

    private async Task<StoredDownload> BuildReceiptDownloadAsync(
        FeePayment payment,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var invoice = await db.FeeInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(i => i.TenantId == tenantId && i.Id == payment.InvoiceId, cancellationToken);

        var student = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(s => s.TenantId == tenantId && s.Id == payment.StudentId, cancellationToken);

        string? batchName = null;
        if (invoice.BatchId.HasValue)
        {
            batchName = await db.AcademicBatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => b.TenantId == tenantId && b.Id == invoice.BatchId.Value)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var branding = await LoadBrandingAsync(tenantId, cancellationToken);
        var content = new ReceiptPdfContent(
            InstituteName: branding.Name,
            InstituteAddress: branding.Address,
            InstitutePhone: branding.Phone,
            PrimaryHex: branding.PrimaryHex,
            Logo: branding.Logo,
            ReceiptNumber: payment.ReceiptNumber,
            InvoiceNumber: invoice.InvoiceNumber,
            Title: invoice.Title,
            BillingPeriod: invoice.BillingPeriod,
            StudentName: student.FullName,
            StudentCode: student.StudentCode,
            GuardianName: student.GuardianName,
            BatchName: batchName,
            InvoiceNetAmount: invoice.NetAmount,
            AmountPaid: FeeRules.EffectivePaymentAmount(payment.Amount, payment.RefundedAmount, payment.Status),
            InvoiceBalanceAfter: invoice.BalanceAmount,
            Currency: payment.Currency,
            PaymentMethod: payment.Method.ToString(),
            ReferenceNumber: payment.ReferenceNumber,
            PaidAt: payment.PaidAt,
            Status: payment.Status.ToString(),
            Notes: payment.Notes);

        var pdfBytes = feeDocuments.BuildReceiptPdf(content);
        return new StoredDownload(pdfBytes, "application/pdf", $"{payment.ReceiptNumber.ToLowerInvariant()}.pdf");
    }

    private PayHereCheckoutSessionDto BuildCheckoutSessionDto(
        PayHereCheckoutSession session,
        FeeInvoice invoice,
        Student student,
        Tenant tenant,
        TenantPaymentGatewayConfig? config,
        string merchantSecret,
        string? returnUrlOverride,
        string? cancelUrlOverride,
        string? receiptNumber)
    {
        var merchantId = config?.MerchantId ?? "SANDBOX_MERCHANT";
        var hash = !string.IsNullOrWhiteSpace(merchantSecret)
            ? paymentGateway.GenerateCheckoutHash(merchantId, merchantSecret, session.OrderId, session.Amount, session.Currency)
            : string.Empty;

        var nameParts = student.GuardianName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : student.FullName;
        var lastName = nameParts.Length > 1 ? nameParts[1] : "Parent";

        var returnUrl = NullIfBlank(returnUrlOverride)
            ?? config?.ReturnUrlOverride
            ?? $"/pay/{session.OrderId}";
        var cancelUrl = NullIfBlank(cancelUrlOverride)
            ?? config?.CancelUrlOverride
            ?? $"/pay/{session.OrderId}";
        var notifyUrl = config?.NotifyUrlOverride
            ?? "/api/fees/payhere/webhook";

        var formFields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["merchant_id"] = merchantId,
            ["return_url"] = returnUrl,
            ["cancel_url"] = cancelUrl,
            ["notify_url"] = notifyUrl,
            ["order_id"] = session.OrderId,
            ["items"] = invoice.Title,
            ["currency"] = session.Currency,
            ["amount"] = paymentGateway.FormatAmount(session.Amount),
            ["first_name"] = firstName,
            ["last_name"] = lastName,
            ["email"] = tenant.ContactEmail ?? "parent@eduflow.lk",
            ["phone"] = student.GuardianPhone,
            ["address"] = student.Address,
            ["city"] = student.City ?? "Colombo",
            ["country"] = "Sri Lanka",
            ["hash"] = hash,
            ["custom_1"] = session.TenantId.ToString()
        };

        return new PayHereCheckoutSessionDto(
            SessionId: session.Id,
            OrderId: session.OrderId,
            InvoiceId: invoice.Id,
            InvoiceNumber: invoice.InvoiceNumber,
            InvoiceTitle: invoice.Title,
            StudentId: student.Id,
            StudentName: student.FullName,
            StudentCode: student.StudentCode,
            Amount: session.Amount,
            InvoiceNetAmount: invoice.NetAmount,
            InvoiceBalanceAmount: invoice.BalanceAmount,
            Currency: session.Currency,
            IsSandbox: session.IsSandbox,
            CheckoutUrl: paymentGateway.GetCheckoutUrl(session.IsSandbox),
            Status: session.Status.ToString(),
            PayHerePaymentId: session.PayHerePaymentId,
            FeePaymentId: session.FeePaymentId,
            ReceiptNumber: receiptNumber,
            FormFields: formFields,
            CreatedAt: session.CreatedAt,
            CompletedAt: session.CompletedAt);
    }

    private IQueryable<FeeInvoice> BuildFilteredInvoicesQuery(FeeInvoiceListQuery query)
    {
        var invoices = db.FeeInvoices.AsNoTracking();

        if (query.StudentId.HasValue)
        {
            invoices = invoices.Where(i => i.StudentId == query.StudentId.Value);
        }

        if (query.BatchId.HasValue)
        {
            invoices = invoices.Where(i => i.BatchId == query.BatchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.BillingPeriod))
        {
            var period = query.BillingPeriod.Trim();
            invoices = invoices.Where(i => i.BillingPeriod == period);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<FeeInvoiceStatus>(query.Status.Trim(), ignoreCase: true, out var parsedStatus))
            {
                throw new DomainValidationException("INVALID_INVOICE_STATUS", "Status must be Pending, PartiallyPaid, Paid, or Voided.");
            }

            invoices = invoices.Where(i => i.Status == parsedStatus);
        }

        if (query.OverdueOnly == true)
        {
            var today = ColomboClock.Today();
            invoices = invoices.Where(i =>
                (i.Status == FeeInvoiceStatus.Pending || i.Status == FeeInvoiceStatus.PartiallyPaid)
                && i.BalanceAmount > 0m
                && i.DueDate < today);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var matchingStudents = db.Students
                .AsNoTracking()
                .Where(s => s.FullName.Contains(term) || s.StudentCode.Contains(term) || s.GuardianPhone.Contains(term))
                .Select(s => s.Id);

            invoices = invoices.Where(i =>
                i.InvoiceNumber.Contains(term)
                || i.Title.Contains(term)
                || matchingStudents.Contains(i.StudentId));
        }

        return invoices;
    }

    private async Task<IReadOnlyList<FeeStructureDto>> MapFeeStructuresAsync(
        IReadOnlyList<FeeStructure> structures,
        CancellationToken cancellationToken)
    {
        if (structures.Count == 0)
        {
            return [];
        }

        var levelIds = structures.Where(s => s.EducationLevelId.HasValue).Select(s => s.EducationLevelId!.Value).Distinct().ToList();
        var levels = await db.EducationLevels
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var batchIds = structures.Where(s => s.BatchId.HasValue).Select(s => s.BatchId!.Value).Distinct().ToList();
        var batches = await db.AcademicBatches
            .AsNoTracking()
            .Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        return structures.Select(s => new FeeStructureDto(
            s.Id,
            s.Name,
            s.Frequency.ToString(),
            s.Amount,
            s.Currency,
            s.EducationLevelId,
            s.EducationLevelId.HasValue && levels.TryGetValue(s.EducationLevelId.Value, out var lName) ? lName : null,
            s.BatchId,
            s.BatchId.HasValue && batches.TryGetValue(s.BatchId.Value, out var bName) ? bName : null,
            s.DueDayOfMonth,
            s.Description,
            s.IsActive,
            s.CreatedAt)).ToList();
    }

    private async Task<IReadOnlyList<StudentConcessionDto>> MapConcessionsAsync(
        IReadOnlyList<StudentConcession> concessions,
        CancellationToken cancellationToken)
    {
        if (concessions.Count == 0)
        {
            return [];
        }

        var studentIds = concessions.Select(c => c.StudentId).Distinct().ToList();
        var students = await db.Students
            .AsNoTracking()
            .Where(s => studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var structureIds = concessions.Where(c => c.FeeStructureId.HasValue).Select(c => c.FeeStructureId!.Value).Distinct().ToList();
        var structures = await db.FeeStructures
            .AsNoTracking()
            .Where(f => structureIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);

        return concessions.Select(c =>
        {
            students.TryGetValue(c.StudentId, out var student);
            return new StudentConcessionDto(
                c.Id,
                c.StudentId,
                student?.StudentCode ?? string.Empty,
                student?.FullName ?? "Student",
                c.FeeStructureId,
                c.FeeStructureId.HasValue && structures.TryGetValue(c.FeeStructureId.Value, out var fName) ? fName : null,
                c.Name,
                c.DiscountType.ToString(),
                c.Value,
                c.Reason,
                c.ValidFrom,
                c.ValidTo,
                c.IsActive,
                c.CreatedAt);
        }).ToList();
    }

    private async Task<IReadOnlyList<FeeInvoiceDto>> MapInvoicesAsync(
        IReadOnlyList<FeeInvoice> invoices,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (invoices.Count == 0)
        {
            return [];
        }

        var today = ColomboClock.Today();
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var studentIds = invoices.Select(i => i.StudentId).Distinct().ToList();
        var structureIds = invoices.Select(i => i.FeeStructureId).Distinct().ToList();
        var batchIds = invoices.Where(i => i.BatchId.HasValue).Select(i => i.BatchId!.Value).Distinct().ToList();
        var levelIds = invoices.Where(i => i.EducationLevelId.HasValue).Select(i => i.EducationLevelId!.Value).Distinct().ToList();

        var students = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var structures = await db.FeeStructures
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && structureIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var batches = await db.AcademicBatches
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId && batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => b.Name, cancellationToken);

        var levels = await db.EducationLevels
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => levelIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        var payments = await db.FeePayments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => !p.IsDeleted && p.TenantId == tenantId && invoiceIds.Contains(p.InvoiceId))
            .OrderByDescending(p => p.PaidAt)
            .ThenByDescending(p => p.ReceiptSequence)
            .ToListAsync(cancellationToken);

        var mappedPayments = await MapPaymentsAsync(payments, tenantId, cancellationToken);
        var paymentsByInvoice = mappedPayments
            .GroupBy(p => p.InvoiceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<FeePaymentDto>)g.ToList());

        return invoices.Select(i =>
        {
            students.TryGetValue(i.StudentId, out var student);
            structures.TryGetValue(i.FeeStructureId, out var structure);
            var daysOverdue = FeeRules.DaysOverdue(today, i.DueDate, i.BalanceAmount, i.Status);
            paymentsByInvoice.TryGetValue(i.Id, out var invPayments);

            return new FeeInvoiceDto(
                i.Id,
                i.InvoiceNumber,
                i.StudentId,
                student?.StudentCode ?? string.Empty,
                student?.FullName ?? "Student",
                student?.GuardianName ?? string.Empty,
                student?.GuardianPhone ?? string.Empty,
                i.FeeStructureId,
                structure?.Name ?? i.Title,
                structure?.Frequency.ToString() ?? nameof(FeeFrequency.Monthly),
                i.BatchId,
                i.BatchId.HasValue && batches.TryGetValue(i.BatchId.Value, out var bName) ? bName : null,
                i.EducationLevelId,
                i.EducationLevelId.HasValue && levels.TryGetValue(i.EducationLevelId.Value, out var lName) ? lName : null,
                i.BillingPeriod,
                i.Title,
                i.GrossAmount,
                i.DiscountAmount,
                i.ConcessionId,
                i.ConcessionDescription,
                i.NetAmount,
                i.PaidAmount,
                i.BalanceAmount,
                i.Currency,
                i.IssueDate,
                i.DueDate,
                i.Status.ToString(),
                daysOverdue > 0,
                daysOverdue,
                i.PaidAt,
                i.VoidedAt,
                i.VoidReason,
                i.Notes,
                invPayments ?? [],
                i.CreatedAt);
        }).ToList();
    }

    private async Task<IReadOnlyList<FeePaymentDto>> MapPaymentsAsync(
        IReadOnlyList<FeePayment> payments,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (payments.Count == 0)
        {
            return [];
        }

        var invoiceIds = payments.Select(p => p.InvoiceId).Distinct().ToList();
        var studentIds = payments.Select(p => p.StudentId).Distinct().ToList();

        var invoices = await db.FeeInvoices
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && invoiceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.InvoiceNumber, cancellationToken);

        var students = await db.Students
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && studentIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        return payments.Select(p =>
        {
            students.TryGetValue(p.StudentId, out var student);
            invoices.TryGetValue(p.InvoiceId, out var invNo);
            return new FeePaymentDto(
                p.Id,
                p.InvoiceId,
                invNo ?? string.Empty,
                p.StudentId,
                student?.StudentCode ?? string.Empty,
                student?.FullName ?? "Student",
                p.ReceiptSequence,
                p.ReceiptNumber,
                p.Amount,
                p.RefundedAmount,
                FeeRules.EffectivePaymentAmount(p.Amount, p.RefundedAmount, p.Status),
                p.Currency,
                p.Method.ToString(),
                p.Status.ToString(),
                p.ReferenceNumber,
                p.AttachmentUrl,
                p.PaidAt,
                p.Notes,
                p.VoidedOrRefundedAt,
                p.VoidOrRefundReason,
                p.CreatedAt);
        }).ToList();
    }

    private static StudentConcession? ResolveBestConcession(
        IReadOnlyList<StudentConcession> concessions,
        Guid studentId,
        Guid feeStructureId,
        decimal grossAmount,
        DateOnly today)
    {
        var applicable = concessions
            .Where(c => c.IsActive
                        && c.StudentId == studentId
                        && (!c.FeeStructureId.HasValue || c.FeeStructureId.Value == feeStructureId)
                        && (!c.ValidFrom.HasValue || c.ValidFrom.Value <= today)
                        && (!c.ValidTo.HasValue || c.ValidTo.Value >= today))
            .ToList();

        if (applicable.Count == 0)
        {
            return null;
        }

        return applicable
            .OrderByDescending(c => c.FeeStructureId == feeStructureId ? 1 : 0)
            .ThenByDescending(c => FeeRules.CalculateDiscount(grossAmount, c.DiscountType, c.Value))
            .First();
    }

    private async Task<int> NextReceiptSequenceUnlockedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var maxSeq = await db.FeePayments
            .IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .Select(p => (int?)p.ReceiptSequence)
            .MaxAsync(cancellationToken) ?? 0;

        return maxSeq + 1;
    }

    private async Task<int> NextInvoiceSequenceUnlockedAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var invoiceNumbers = await db.FeeInvoices
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId)
            .Select(i => i.InvoiceNumber)
            .ToListAsync(cancellationToken);

        var maxSeq = invoiceNumbers.Count == 0
            ? 0
            : invoiceNumbers.Max(FeeRules.ParseSequence);

        return maxSeq + 1;
    }

    private async Task<(string Name, decimal Amount, string Currency, Guid? LevelId, Guid? BatchId, int DueDay, string? Description)>
        ValidateFeeStructureAsync(UpsertFeeStructureRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 150)
        {
            throw new DomainValidationException("INVALID_NAME", "Fee structure name must be between 1 and 150 characters.");
        }

        var amount = FeeRules.RoundMoney(request.Amount);
        if (amount <= 0m)
        {
            throw new DomainValidationException("INVALID_AMOUNT", "Fee amount must be greater than zero.");
        }

        if (request.DueDayOfMonth is < 1 or > 28)
        {
            throw new DomainValidationException("INVALID_DUE_DAY", "Due day of month must be between 1 and 28.");
        }

        if (!request.BatchId.HasValue && !request.EducationLevelId.HasValue)
        {
            throw new DomainValidationException(
                "TARGET_REQUIRED",
                "Select an education level or a batch for the fee structure.");
        }

        Guid? resolvedLevelId = request.EducationLevelId;
        if (request.BatchId.HasValue)
        {
            var batch = await db.AcademicBatches
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == request.BatchId.Value, cancellationToken)
                ?? throw new EntityNotFoundException(nameof(AcademicBatch), request.BatchId.Value.ToString());

            if (resolvedLevelId.HasValue && resolvedLevelId.Value != batch.EducationLevelId)
            {
                throw new DomainValidationException("BATCH_LEVEL_MISMATCH", "Selected batch does not belong to the selected education level.");
            }

            resolvedLevelId = batch.EducationLevelId;
        }
        else if (resolvedLevelId.HasValue)
        {
            var levelEnabled = await db.TenantEducationLevels
                .AsNoTracking()
                .AnyAsync(l => l.EducationLevelId == resolvedLevelId.Value && l.IsEnabled, cancellationToken);
            if (!levelEnabled)
            {
                throw new DomainValidationException("LEVEL_NOT_ENABLED", "Selected education level is not enabled for this institute.");
            }
        }

        var currency = string.IsNullOrWhiteSpace(request.Currency) ? "LKR" : request.Currency.Trim().ToUpperInvariant();
        var description = NullIfBlank(request.Description);
        if (description is { Length: > 500 })
        {
            throw new DomainValidationException("INVALID_DESCRIPTION", "Description must be 500 characters or fewer.");
        }

        return (name, amount, currency, resolvedLevelId, request.BatchId, request.DueDayOfMonth, description);
    }

    private async Task<(string Name, decimal Value, string? Reason)> ValidateConcessionAsync(
        UpsertStudentConcessionRequest request,
        CancellationToken cancellationToken)
    {
        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 150)
        {
            throw new DomainValidationException("INVALID_NAME", "Concession name must be between 1 and 150 characters.");
        }

        var value = FeeRules.RoundMoney(request.Value);
        if (value <= 0m)
        {
            throw new DomainValidationException("INVALID_CONCESSION_VALUE", "Concession value must be greater than zero.");
        }

        if (request.DiscountType == DiscountType.Percentage && value > 100m)
        {
            throw new DomainValidationException("INVALID_CONCESSION_VALUE", "Percentage concession cannot exceed 100%.");
        }

        if (request.ValidFrom.HasValue && request.ValidTo.HasValue && request.ValidFrom.Value > request.ValidTo.Value)
        {
            throw new DomainValidationException("INVALID_DATE_RANGE", "ValidFrom must be on or before ValidTo.");
        }

        var studentExists = await db.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken);
        if (!studentExists)
        {
            throw new EntityNotFoundException(nameof(Student), request.StudentId.ToString());
        }

        if (request.FeeStructureId.HasValue)
        {
            var structureExists = await db.FeeStructures.AnyAsync(f => f.Id == request.FeeStructureId.Value, cancellationToken);
            if (!structureExists)
            {
                throw new EntityNotFoundException(nameof(FeeStructure), request.FeeStructureId.Value.ToString());
            }
        }

        var reason = NullIfBlank(request.Reason);
        if (reason is { Length: > 300 })
        {
            throw new DomainValidationException("INVALID_REASON", "Reason must be 300 characters or fewer.");
        }

        return (name, value, reason);
    }

    private async Task EnsureOnlinePaymentsEnabledAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var eval = await featureService.EvaluateAsync(tenantId, FeatureKey.OnlinePayments, cancellationToken);
        if (!eval.IsInPlan)
        {
            throw new FeatureNotInPlanException(FeatureKey.OnlinePayments);
        }

        if (!eval.IsEnabled)
        {
            throw new FeatureDisabledException(FeatureKey.OnlinePayments);
        }
    }

    private async Task<(string Name, string? Address, string? Phone, string PrimaryHex, byte[]? Logo)> LoadBrandingAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var profile = await db.BrandingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.TenantId == tenantId, cancellationToken);

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(t => t.Id == tenantId, cancellationToken);

        var name = string.IsNullOrWhiteSpace(profile?.InstituteDisplayName) ? tenant.Name : profile!.InstituteDisplayName;
        var primary = string.IsNullOrWhiteSpace(profile?.PrimaryColor) ? BrandingProfile.DefaultPrimaryColor : profile!.PrimaryColor;

        byte[]? logo = null;
        if (!string.IsNullOrWhiteSpace(profile?.LogoUrl))
        {
            var opened = fileStorage.OpenRead(profile.LogoUrl);
            if (opened is not null)
            {
                await using (opened)
                {
                    using var memory = new MemoryStream();
                    await opened.CopyToAsync(memory, cancellationToken);
                    var bytes = memory.ToArray();
                    var jpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                    var png = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50;
                    if (jpeg || png)
                    {
                        logo = bytes;
                    }
                }
            }
        }

        return (name, tenant.Address, tenant.ContactPhone, primary, logo);
    }

    private static string ResolveAttachmentExtension(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var allowed = contentType?.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => ext is ".pdf" or ".png" or ".webp" or ".jpg" or ".jpeg" ? ext : null
        };

        if (allowed == ".jpeg")
        {
            allowed = ".jpg";
        }

        return allowed ?? throw new DomainValidationException("INVALID_ATTACHMENT_TYPE", "Upload a PDF, PNG, JPEG, or WebP payment slip.");
    }

    private static string NormalizeRequiredReason(string? reason, string message)
    {
        var trimmed = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainValidationException("REASON_REQUIRED", message);
        }

        if (trimmed.Length > 300)
        {
            throw new DomainValidationException("INVALID_REASON", "Reason must be 300 characters or fewer.");
        }

        return trimmed;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
