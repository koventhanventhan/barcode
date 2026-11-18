using EduFlow.Domain.Enums;

namespace EduFlow.Domain.Constants;

public sealed record DefaultTemplateText(string Subject, string Body);

public sealed record NotificationTypeDefinition(
    NotificationMessageType MessageType,
    FeatureKey? ParentFeatureKey,
    bool DefaultSmsEnabled,
    bool DefaultEmailEnabled,
    bool DefaultWhatsAppEnabled,
    bool ExemptFromQuietHours,
    IReadOnlyList<string> Variables,
    IReadOnlyDictionary<string, DefaultTemplateText> DefaultTemplates,
    SmsMessageCategory SmsCategory = SmsMessageCategory.Transactional);

public static class NotificationCatalog
{
    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "ta", "si"];

    public static readonly IReadOnlyDictionary<string, string> SampleVariables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["studentName"] = "Nimal Perera",
            ["studentCode"] = "S000001",
            ["className"] = "2026 O/L Combined Maths",
            ["batchName"] = "OL2026",
            ["amount"] = "LKR 3,500.00",
            ["dueDate"] = "2026-11-25",
            ["receiptNo"] = "RCP-000142",
            ["examName"] = "Term 3 Combined Maths",
            ["examDate"] = "2026-12-05",
            ["marks"] = "86%",
            ["grade"] = "A",
            ["instituteName"] = "Royal Vidyalaya",
            ["date"] = "2026-11-17",
            ["time"] = "04:05 PM",
            ["message"] = "Classes on Poya day will be held online at 8:00 AM.",
            ["presentCount"] = "142",
            ["absentCount"] = "14",
            ["backupStatus"] = "Completed"
        };

    public static readonly IReadOnlyList<NotificationTypeDefinition> All =
    [
        new(
            NotificationMessageType.AttendancePresent,
            FeatureKey.AttendanceSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{studentCode}", "{className}", "{time}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Attendance recorded for {studentName}",
                    "{instituteName}: {studentName} marked present for {className} at {time} on {date}."),
                ["ta"] = new(
                    "{studentName} வருகை பதிவு",
                    "{instituteName}: {studentName} {date} அன்று {time} மணிக்கு {className} வகுப்பில் வருகை பதிவு செய்யப்பட்டது."),
                ["si"] = new(
                    "{studentName} පැමිණීම සටහන් විය",
                    "{instituteName}: {studentName} {date} දින {time} ට {className} පන්තියට පැමිණි බව සටහන් විය.")
            }),
        new(
            NotificationMessageType.AttendanceAbsent,
            FeatureKey.AttendanceSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{studentCode}", "{className}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Absence alert for {studentName}",
                    "{instituteName}: {studentName} was absent from {className} on {date}."),
                ["ta"] = new(
                    "{studentName} வருகையின்மை அறிவிப்பு",
                    "{instituteName}: {studentName} {date} அன்று {className} வகுப்பிற்கு சமூகமளிக்கவில்லை."),
                ["si"] = new(
                    "{studentName} නොපැමිණීමේ දැනුම්දීම",
                    "{instituteName}: {studentName} {date} දින {className} පන්තියට සහභාගී නොවීය.")
            }),
        new(
            NotificationMessageType.ClassStarted,
            FeatureKey.ClassSessionSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{className}", "{time}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "{className} has started",
                    "{instituteName}: {className} started at {time} on {date}."),
                ["ta"] = new(
                    "{className} வகுப்பு ஆரம்பமானது",
                    "{instituteName}: {className} வகுப்பு {date} அன்று {time} மணிக்கு ஆரம்பமானது."),
                ["si"] = new(
                    "{className} පන්තිය ආරම්භ විය",
                    "{instituteName}: {className} පන්තිය {date} දින {time} ට ආරම්භ විය.")
            }),
        new(
            NotificationMessageType.ClassStopped,
            FeatureKey.ClassSessionSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{className}", "{time}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "{className} has ended",
                    "{instituteName}: {className} ended at {time} on {date}."),
                ["ta"] = new(
                    "{className} வகுப்பு நிறைவடைந்தது",
                    "{instituteName}: {className} வகுப்பு {date} அன்று {time} மணிக்கு நிறைவடைந்தது."),
                ["si"] = new(
                    "{className} පන්තිය අවසන් විය",
                    "{instituteName}: {className} පන්තිය {date} දින {time} ට අවසන් විය.")
            }),
        new(
            NotificationMessageType.ClassCancelled,
            FeatureKey.ClassSessionSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{className}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "{className} cancelled on {date}",
                    "{instituteName}: {className} scheduled for {date} has been cancelled."),
                ["ta"] = new(
                    "{className} வகுப்பு இரத்து செய்யப்பட்டது",
                    "{instituteName}: {date} அன்று நடைபெறவிருந்த {className} வகுப்பு இரத்து செய்யப்பட்டது."),
                ["si"] = new(
                    "{className} පන්තිය අවලංගු කරන ලදී",
                    "{instituteName}: {date} දින පැවැත්වීමට නියමිතව තිබූ {className} පන්තිය අවලංගු කරන ලදී.")
            }),
        new(
            NotificationMessageType.FeeDueReminder,
            FeatureKey.FeeReminderSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{className}", "{amount}", "{dueDate}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Fee reminder for {studentName}",
                    "{instituteName}: Fee of {amount} for {studentName} ({className}) is due on {dueDate}."),
                ["ta"] = new(
                    "{studentName} கட்டண நினைவூட்டல்",
                    "{instituteName}: {studentName} ({className}) கட்டணம் {amount} {dueDate} அன்று செலுத்தப்பட வேண்டும்."),
                ["si"] = new(
                    "{studentName} ගාස්තු සිහිකැඳවීම",
                    "{instituteName}: {studentName} ({className}) සඳහා ගාස්තුව {amount} ක් {dueDate} දිනට ගෙවිය යුතුය.")
            }),
        new(
            NotificationMessageType.PaymentReceivedParent,
            FeatureKey.PaymentConfirmationSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: true,
            Variables: ["{studentName}", "{amount}", "{receiptNo}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Payment received for {studentName}",
                    "{instituteName}: Received {amount} for {studentName}. Receipt {receiptNo}. Thank you."),
                ["ta"] = new(
                    "{studentName} கட்டணம் பெறப்பட்டது",
                    "{instituteName}: {studentName} க்கான {amount} பெறப்பட்டது. ரசீது {receiptNo}. நன்றி."),
                ["si"] = new(
                    "{studentName} ගෙවීම ලැබුණි",
                    "{instituteName}: {studentName} සඳහා {amount} ලැබුණි. රිසිට්පත {receiptNo}. ස්තූතියි.")
            }),
        new(
            NotificationMessageType.PaymentReceivedAdmin,
            FeatureKey.PaymentConfirmationSms,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: true,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: true,
            Variables: ["{studentName}", "{amount}", "{receiptNo}", "{date}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Fee collection: {amount} from {studentName}",
                    "{instituteName}: Payment of {amount} recorded for {studentName} (Receipt {receiptNo})."),
                ["ta"] = new(
                    "கட்டண வசூல்: {studentName} - {amount}",
                    "{instituteName}: {studentName} இடமிருந்து {amount} கட்டணம் பெறப்பட்டது (ரசீது {receiptNo})."),
                ["si"] = new(
                    "ගාස්තු ලැබීම: {studentName} - {amount}",
                    "{instituteName}: {studentName} වෙනුවෙන් {amount} ගෙවීමක් සටහන් විය (රිසිට්පත {receiptNo}).")
            }),
        new(
            NotificationMessageType.ExamScheduled,
            FeatureKey.ExamNotifications,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{examName}", "{examDate}", "{className}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Exam scheduled: {examName}",
                    "{instituteName}: {examName} for {className} is scheduled on {examDate}."),
                ["ta"] = new(
                    "பரீட்சை அறிவிப்பு: {examName}",
                    "{instituteName}: {className} க்கான {examName} பரீட்சை {examDate} அன்று நடைபெறும்."),
                ["si"] = new(
                    "විභාග දැනුම්දීම: {examName}",
                    "{instituteName}: {className} සඳහා {examName} විභාගය {examDate} දින පැවැත්වේ.")
            }),
        new(
            NotificationMessageType.ExamReminder,
            FeatureKey.ExamNotifications,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{examName}", "{examDate}", "{className}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Reminder: {examName} on {examDate}",
                    "{instituteName}: Reminder that {examName} for {studentName} is on {examDate}."),
                ["ta"] = new(
                    "நினைவூட்டல்: {examName}",
                    "{instituteName}: {studentName} க்கான {examName} பரீட்சை {examDate} அன்று நடைபெறும்."),
                ["si"] = new(
                    "සිහිකැඳවීම: {examName}",
                    "{instituteName}: {studentName} සඳහා {examName} විභාගය {examDate} දින පැවැත්වෙන බව සිහිපත් කරමු.")
            }),
        new(
            NotificationMessageType.MarksReleased,
            FeatureKey.ExamNotifications,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{examName}", "{marks}", "{grade}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Results published for {examName}",
                    "{instituteName}: {examName} results for {studentName}: {marks}."),
                ["ta"] = new(
                    "{examName} பெறுபேறுகள் வெளியிடப்பட்டன",
                    "{instituteName}: {studentName} — {examName}: {marks}."),
                ["si"] = new(
                    "{examName} ප්‍රතිඵල නිකුත් කර ඇත",
                    "{instituteName}: {studentName} — {examName}: {marks}.")
            }),
        new(
            NotificationMessageType.Broadcast,
            FeatureKey.BroadcastMessages,
            DefaultSmsEnabled: true,
            DefaultEmailEnabled: false,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{studentName}", "{className}", "{message}", "{instituteName}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Announcement from {instituteName}",
                    "{instituteName}: {message}"),
                ["ta"] = new(
                    "{instituteName} அறிவிப்பு",
                    "{instituteName}: {message}"),
                ["si"] = new(
                    "{instituteName} නිවේදනය",
                    "{instituteName}: {message}")
            },
            SmsCategory: SmsMessageCategory.Promotional),
        new(
            NotificationMessageType.BackupResultAdmin,
            FeatureKey.AutoBackup,
            DefaultSmsEnabled: false,
            DefaultEmailEnabled: true,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{instituteName}", "{date}", "{backupStatus}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Backup status for {instituteName}",
                    "{instituteName}: Scheduled backup on {date} finished with status: {backupStatus}."),
                ["ta"] = new(
                    "{instituteName} காப்புப்பிரதி நிலை",
                    "{instituteName}: {date} அன்று காப்புப்பிரதி நிலை: {backupStatus}."),
                ["si"] = new(
                    "{instituteName} උපස්ථ තත්ත්වය",
                    "{instituteName}: {date} දින උපස්ථ ක්‍රියාවලිය අවසන් විය: {backupStatus}.")
            }),
        new(
            NotificationMessageType.DailySummaryAdmin,
            ParentFeatureKey: null,
            DefaultSmsEnabled: false,
            DefaultEmailEnabled: true,
            DefaultWhatsAppEnabled: false,
            ExemptFromQuietHours: false,
            Variables: ["{instituteName}", "{date}", "{presentCount}", "{absentCount}", "{amount}"],
            DefaultTemplates: new Dictionary<string, DefaultTemplateText>(StringComparer.OrdinalIgnoreCase)
            {
                ["en"] = new(
                    "Daily summary for {instituteName} ({date})",
                    "{instituteName} summary for {date}: Present {presentCount}, Absent {absentCount}, Collected {amount}."),
                ["ta"] = new(
                    "{instituteName} தினசரி சுருக்கம் ({date})",
                    "{instituteName} {date} சுருக்கம்: வருகை {presentCount}, சமூகமளிக்காதோர் {absentCount}, வசூல் {amount}."),
                ["si"] = new(
                    "{instituteName} දෛනික සාරාංශය ({date})",
                    "{instituteName} {date} සාරාංශය: පැමිණි {presentCount}, නොපැමිණි {absentCount}, එකතුව {amount}.")
            })
    ];

    private static readonly IReadOnlyDictionary<NotificationMessageType, NotificationTypeDefinition> ByType =
        All.ToDictionary(d => d.MessageType);

    public static NotificationTypeDefinition GetDefinition(NotificationMessageType messageType) =>
        ByType[messageType];

    public static FeatureKey? GetChannelFeatureKey(NotificationChannel channel) =>
        channel switch
        {
            NotificationChannel.Email => FeatureKey.EmailNotifications,
            NotificationChannel.WhatsApp => FeatureKey.WhatsAppMessaging,
            _ => null
        };

    public static DefaultTemplateText GetDefaultTemplate(NotificationMessageType messageType, string language)
    {
        var def = GetDefinition(messageType);
        var normalized = NormalizeLanguage(language);
        return def.DefaultTemplates.TryGetValue(normalized, out var template)
            ? template
            : def.DefaultTemplates["en"];
    }

    public static string NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return "en";
        }

        var trimmed = language.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("ta"))
        {
            return "ta";
        }

        if (trimmed.StartsWith("si"))
        {
            return "si";
        }

        return "en";
    }
}
