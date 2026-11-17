using EduFlow.Application.Academics;
using EduFlow.Domain.Enums;
using FluentAssertions;

namespace EduFlow.UnitTests;

public sealed class TimetableRulesTests
{
    [Fact]
    public void Detects_teacher_batch_and_room_clashes_on_overlapping_weekday_slots()
    {
        var teacherA = Guid.NewGuid();
        var teacherB = Guid.NewGuid();
        var batchA = Guid.NewGuid();
        var batchB = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        var existing = new List<ExistingTimetableSlot>
        {
            new(
                existingId,
                "Combined Maths · OL2026",
                batchA,
                "OL2026",
                subjectId,
                "Combined Maths",
                teacherA,
                "Amali Fernando",
                Weekday: 1,
                StartMinutes: 960, // 16:00
                EndMinutes: 1080,  // 18:00
                Room: "Hall 1",
                IsActive: true)
        };

        // 1. Teacher clash (same teacher, different batch and room, overlapping 17:00-19:00)
        var teacherClash = TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                ExcludeScheduleId: null,
                BatchId: batchB,
                BatchName: "AL2027",
                TeacherUserId: teacherA,
                TeacherName: "Amali Fernando",
                Weekday: 1,
                StartMinutes: 1020,
                EndMinutes: 1140,
                Room: "Hall 2"),
            existing);

        teacherClash.Should().ContainSingle(c => c.ClashType == TimetableClashType.Teacher && c.ConflictingScheduleId == existingId);

        // 2. Batch clash (same batch, different teacher and room, overlapping 15:30-16:30)
        var batchClash = TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                ExcludeScheduleId: null,
                BatchId: batchA,
                BatchName: "OL2026",
                TeacherUserId: teacherB,
                TeacherName: "Kasun Perera",
                Weekday: 1,
                StartMinutes: 930,
                EndMinutes: 990,
                Room: "Hall 2"),
            existing);

        batchClash.Should().ContainSingle(c => c.ClashType == TimetableClashType.Batch && c.ConflictingScheduleId == existingId);

        // 3. Room clash (case-insensitive room match, different teacher and batch)
        var roomClash = TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                ExcludeScheduleId: null,
                BatchId: batchB,
                BatchName: "AL2027",
                TeacherUserId: teacherB,
                TeacherName: "Kasun Perera",
                Weekday: 1,
                StartMinutes: 960,
                EndMinutes: 1020,
                Room: "  hall 1 "),
            existing);

        roomClash.Should().ContainSingle(c => c.ClashType == TimetableClashType.Room && c.ConflictingScheduleId == existingId);
    }

    [Fact]
    public void Adjacent_non_overlapping_times_or_different_weekdays_do_not_clash()
    {
        var teacherId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var existing = new List<ExistingTimetableSlot>
        {
            new(
                Guid.NewGuid(),
                "Morning Class",
                batchId,
                "OL2026",
                Guid.NewGuid(),
                "Maths",
                teacherId,
                "Amali Fernando",
                Weekday: 2,
                StartMinutes: 480, // 08:00
                EndMinutes: 600,  // 10:00
                Room: "Room A",
                IsActive: true)
        };

        // Starts right at 10:00 when previous ends at 10:00
        var backToBack = TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                null,
                batchId,
                "OL2026",
                teacherId,
                "Amali Fernando",
                Weekday: 2,
                StartMinutes: 600,
                EndMinutes: 720,
                Room: "Room A"),
            existing);

        backToBack.Should().BeEmpty();

        // Same time on Wednesday (Weekday 3)
        var differentDay = TimetableRules.DetectClashes(
            new TimetableCandidateSlot(
                null,
                batchId,
                "OL2026",
                teacherId,
                "Amali Fernando",
                Weekday: 3,
                StartMinutes: 480,
                EndMinutes: 600,
                Room: "Room A"),
            existing);

        differentDay.Should().BeEmpty();
    }
}
