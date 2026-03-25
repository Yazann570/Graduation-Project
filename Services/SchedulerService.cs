using Microsoft.EntityFrameworkCore;
using SmartSchedulingSystem.Data;
using SmartSchedulingSystem.Models;

namespace SmartSchedulingSystem.Services
{
    public interface ISchedulerService
    {
        Task<List<RemainingCourseDto>> GetRemainingCoursesAsync(string studentId);
        Task<FilterDto> SaveFilterAsync(string studentId, CreateFilterRequest req);
        Task<List<ScheduleResultDto>> GenerateSchedulesAsync(int filterId, string studentId);
        Task<List<FavouriteDto>> GetFavouritesAsync(string studentId, int filterId);
        Task<bool> ToggleFavouriteAsync(ToggleFavouriteRequest req, string studentId);
        Task<List<FilterDto>> GetAllFiltersAsync(string studentId);
    }

    public class SchedulerService : ISchedulerService
    {
        private readonly AppDbContext _db;

        private static readonly Dictionary<string, string> RequirementLabels = new()
        {
            { "CP", "Compulsory Program Requirements"    },
            { "CS", "Compulsory School Requirements"     },
            { "CU", "Compulsory University Requirements" },
            { "EU", "Elective University Requirements"   },
            { "EP", "Elective Program Requirements"      },
        };

        public SchedulerService(AppDbContext db) => _db = db;

        // ── Helper: get next sequence value from Oracle ───────
        private async Task<int> NextVal(string sequence)
        {
            var conn = _db.Database.GetDbConnection();

            // EF Core may already have the connection open — only open if needed
            bool weOpenedIt = conn.State == System.Data.ConnectionState.Closed;
            if (weOpenedIt)
                await conn.OpenAsync();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT {sequence}.NEXTVAL FROM DUAL";
                var result = await cmd.ExecuteScalarAsync();
                return Convert.ToInt32(result);
            }
            finally
            {
                // Only close it if we were the ones who opened it
                if (weOpenedIt)
                    await conn.CloseAsync();
            }
        }

        // ── 1. Remaining courses ─────────────────────────────
        public async Task<List<RemainingCourseDto>> GetRemainingCoursesAsync(string studentId)
        {
            var doneIds = await _db.StudentCourses
                .Where(sc => sc.StId == studentId)
                .Select(sc => sc.CId)
                .ToListAsync();

            var courses = await _db.Courses
                .Include(c => c.Sections).ThenInclude(s => s.Instructor)
                .Where(c => !doneIds.Contains(c.CId))
                .ToListAsync();

            return courses.Select(c => new RemainingCourseDto
            {
                CId = c.CId,
                CName = c.CName,
                CHrs = c.CHrs,
                CType = c.CType,
                RequirementLabel = RequirementLabels.GetValueOrDefault(c.CType, c.CType),
                IsOnline = c.IsOnline,
                AvailableInstructors = c.Sections
                    .Select(s => s.Instructor)
                    .DistinctBy(i => i.IId)
                    .Select(i => new InstructorSummaryDto { IId = i.IId, IName = i.IName })
                    .ToList()
            }).ToList();
        }

        // ── 2. Save filter ───────────────────────────────────
        public async Task<FilterDto> SaveFilterAsync(string studentId, CreateFilterRequest req)
        {
            var studentExists = await _db.Students.AnyAsync(s => s.StId == studentId);
            if (!studentExists)
                throw new InvalidOperationException(
                    $"Student '{studentId}' not found. Run schema.sql seed data first.");

            // Use raw SQL so Oracle's column DEFAULT doesn't fire a second NEXTVAL.
            // EF Core Add() + SaveChanges() triggers the DEFAULT even with ValueGeneratedNever.
            int filterId = await NextVal("SEQ_FILTER");

            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO FILTER (F_ID, ST_ID, S_TIME, F_TIME, MIN_BREAK, MAX_BREAK) " +
                "VALUES ({0}, {1}, {2}, {3}, {4}, {5})",
                filterId, studentId, req.StartTime, req.EndTime, req.MinBreak, req.MaxBreak);

            // Save preferred days
            foreach (var day in req.Days)
                _db.DayGroupFilters.Add(new DayGroupFilter { FId = filterId, Day = day });
            await _db.SaveChangesAsync();

            // Replace instructor preferences
            var existingPrefs = await _db.InstructorsAdded
                .Where(ia => ia.StId == studentId)
                .ToListAsync();
            _db.InstructorsAdded.RemoveRange(existingPrefs);
            await _db.SaveChangesAsync();

            foreach (var (courseId, iIds) in req.CourseInstructors)
                foreach (var iId in iIds)
                    _db.InstructorsAdded.Add(new InstructorAdded
                    { IId = iId, CId = courseId, StId = studentId });
            await _db.SaveChangesAsync();

            return new FilterDto
            {
                FId = filterId,
                StartTime = req.StartTime,
                EndTime = req.EndTime,
                MinBreak = req.MinBreak,
                MaxBreak = req.MaxBreak,
                Days = req.Days,
            };
        }

        // ── 3. Generate schedules ────────────────────────────
        public async Task<List<ScheduleResultDto>> GenerateSchedulesAsync(int filterId, string studentId)
        {
            // Debug: check what filters actually exist for this student
            var allFilters = await _db.Filters
                .Where(f => f.StId == studentId)
                .Select(f => f.FId)
                .ToListAsync();

            var filter = await _db.Filters
                .Include(f => f.DayGroupFilters)
                .FirstOrDefaultAsync(f => f.FId == filterId && f.StId == studentId)
                ?? throw new KeyNotFoundException(
                    $"Filter {filterId} not found for student '{studentId}'. " +
                    $"Filters in DB for this student: [{string.Join(", ", allFilters)}]");

            var allowedDays = filter.DayGroupFilters.Select(d => d.Day).ToHashSet();
            var filterStart = TimeOnly.Parse(filter.STime);
            var filterEnd = TimeOnly.Parse(filter.FTime);

            var prefs = await _db.InstructorsAdded
                .Where(ia => ia.StId == studentId)
                .GroupBy(ia => ia.CId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(ia => ia.IId).ToHashSet());

            if (prefs.Count == 0) return new List<ScheduleResultDto>();

            var allSections = await _db.Sections
                .Include(s => s.Course)
                .Include(s => s.Instructor)
                .Include(s => s.DayGroupSections)
                .Where(s => prefs.Keys.Contains(s.CId))
                .ToListAsync();

            var candidatesByCourse = allSections
                .Where(s =>
                {
                    if (prefs.TryGetValue(s.CId, out var ids) && !ids.Contains(s.IId)) return false;
                    var secDays = s.DayGroupSections.Select(d => d.Day).ToHashSet();
                    if (!secDays.IsSubsetOf(allowedDays)) return false;
                    var start = TimeOnly.Parse(s.STime);
                    var end = TimeOnly.Parse(s.FTime);
                    return start >= filterStart && end <= filterEnd;
                })
                .GroupBy(s => s.CId)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (candidatesByCourse.Count < prefs.Count)
                return new List<ScheduleResultDto>();

            var results = new List<List<Section>>();
            var courseList = candidatesByCourse.Values.ToList();
            Backtrack(courseList, 0, new List<Section>(), filter, results, maxResults: 10);

            if (results.Count == 0) return new List<ScheduleResultDto>();

            // Delete old data in FK-safe order: FAVOURITE → GENERATED_SECTION → GENERATED_SCHEDULE
            var oldFavs = await _db.Favourites.Where(f => f.FId == filterId).ToListAsync();
            _db.Favourites.RemoveRange(oldFavs);
            await _db.SaveChangesAsync();

            var oldSchedIds = await _db.GeneratedSchedules
                .Where(g => g.FId == filterId).Select(g => g.SchedId).ToListAsync();
            var oldSections = await _db.GeneratedSections
                .Where(gs => oldSchedIds.Contains(gs.SchedId)).ToListAsync();
            _db.GeneratedSections.RemoveRange(oldSections);
            await _db.SaveChangesAsync();

            var oldScheds = await _db.GeneratedSchedules.Where(g => g.FId == filterId).ToListAsync();
            _db.GeneratedSchedules.RemoveRange(oldScheds);
            await _db.SaveChangesAsync();

            // Persist new schedules — fetch each ID from Oracle before insert
            var dtos = new List<ScheduleResultDto>();
            foreach (var combo in results)
            {
                int schedId = await NextVal("SEQ_GENERATED_SCHED");

                await _db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO GENERATED_SCHEDULE (SCHED_ID, F_ID, CREATION_DATE) " +
                    "VALUES ({0}, {1}, SYSDATE)",
                    schedId, filterId);

                foreach (var sec in combo)
                    _db.GeneratedSections.Add(new GeneratedSection
                    { SecId = sec.SecId, SchedId = schedId, CId = sec.CId });
                await _db.SaveChangesAsync();

                var sched = new GeneratedSchedule
                { SchedId = schedId, FId = filterId, CreationDate = DateTime.UtcNow };
                dtos.Add(MapSchedule(sched, combo, isFav: false));
            }
            return dtos;
        }

        // ── 4. Get favourites ────────────────────────────────
        public async Task<List<FavouriteDto>> GetFavouritesAsync(string studentId, int filterId)
        {
            var favs = await _db.Favourites
                .Include(f => f.Filter)
                .Include(f => f.GeneratedSchedule)
                    .ThenInclude(g => g.GeneratedSections)
                        .ThenInclude(gs => gs.Section).ThenInclude(s => s.Instructor)
                .Include(f => f.GeneratedSchedule)
                    .ThenInclude(g => g.GeneratedSections)
                        .ThenInclude(gs => gs.Section).ThenInclude(s => s.DayGroupSections)
                .Include(f => f.GeneratedSchedule)
                    .ThenInclude(g => g.GeneratedSections).ThenInclude(gs => gs.Course)
                .Where(f => f.FId == filterId && f.Filter.StId == studentId)
                .AsSplitQuery()
                .ToListAsync();

            return favs.Select(f =>
            {
                var sections = f.GeneratedSchedule.GeneratedSections.Select(gs => gs.Section).ToList();
                return new FavouriteDto
                {
                    FavId = f.FavId,
                    SchedId = f.SchedId,
                    Schedule = MapSchedule(f.GeneratedSchedule, sections, isFav: true),
                };
            }).ToList();
        }

        // ── 5. Toggle favourite ──────────────────────────────
        public async Task<bool> ToggleFavouriteAsync(ToggleFavouriteRequest req, string studentId)
        {
            var filterExists = await _db.Filters
                .AnyAsync(f => f.FId == req.FilterId && f.StId == studentId);
            if (!filterExists) throw new KeyNotFoundException("Filter not found.");

            var existing = await _db.Favourites
                .FirstOrDefaultAsync(f => f.FId == req.FilterId && f.SchedId == req.ScheduleId);

            if (existing != null)
            {
                _db.Favourites.Remove(existing);
                await _db.SaveChangesAsync();
                return false;
            }

            // Use raw SQL to avoid Oracle DEFAULT firing a second NEXTVAL
            int favId = await NextVal("SEQ_FAV");
            await _db.Database.ExecuteSqlRawAsync(
                "INSERT INTO FAVOURITE (FAV_ID, F_ID, SCHED_ID) VALUES ({0}, {1}, {2})",
                favId, req.FilterId, req.ScheduleId);
            return true;
        }

        // ── 6. Get all filters (debug) ───────────────────
        public async Task<List<FilterDto>> GetAllFiltersAsync(string studentId)
        {
            var filters = await _db.Filters
                .Include(f => f.DayGroupFilters)
                .Where(f => f.StId == studentId)
                .OrderByDescending(f => f.FId)
                .ToListAsync();

            return filters.Select(f => new FilterDto
            {
                FId = f.FId,
                StartTime = f.STime,
                EndTime = f.FTime,
                MinBreak = f.MinBreak,
                MaxBreak = f.MaxBreak,
                Days = f.DayGroupFilters.Select(d => d.Day).ToList(),
            }).ToList();
        }

        // ── Private helpers ──────────────────────────────────
        private static void Backtrack(
            List<List<Section>> byCourse, int idx,
            List<Section> current, Filter filter,
            List<List<Section>> results, int maxResults)
        {
            if (results.Count >= maxResults) return;
            if (idx == byCourse.Count) { results.Add(new List<Section>(current)); return; }

            foreach (var sec in byCourse[idx])
            {
                if (!HasConflict(sec, current, filter.MinBreak, filter.MaxBreak))
                {
                    current.Add(sec);
                    Backtrack(byCourse, idx + 1, current, filter, results, maxResults);
                    current.RemoveAt(current.Count - 1);
                }
            }
        }

        private static bool HasConflict(Section candidate, List<Section> chosen, int minBreak, int maxBreak)
        {
            var candDays = candidate.DayGroupSections.Select(d => d.Day).ToHashSet();
            var candStart = TimeOnly.Parse(candidate.STime);
            var candEnd = TimeOnly.Parse(candidate.FTime);

            foreach (var sec in chosen)
            {
                var secDays = sec.DayGroupSections.Select(d => d.Day).ToHashSet();
                if (!candDays.Intersect(secDays).Any()) continue;

                var secStart = TimeOnly.Parse(sec.STime);
                var secEnd = TimeOnly.Parse(sec.FTime);

                if (candStart < secEnd && candEnd > secStart) return true;

                int gapMin = candStart > secEnd
                    ? (int)(candStart - secEnd).TotalMinutes
                    : (int)(secStart - candEnd).TotalMinutes;

                if (gapMin < minBreak || gapMin > maxBreak) return true;
            }
            return false;
        }

        private static ScheduleResultDto MapSchedule(
            GeneratedSchedule sched, List<Section> sections, bool isFav) => new()
            {
                SchedId = sched.SchedId,
                TotalHours = sections.Sum(s => s.Course.CHrs),
                IsFavourite = isFav,
                CreatedAt = sched.CreationDate,
                Courses = sections.Select((s, i) => new ScheduledCourseDto
                {
                    CourseNumber = s.CId,
                    CourseName = s.Course.CName,
                    CType = s.Course.CType,
                    RequirementLabel = RequirementLabels.GetValueOrDefault(s.Course.CType, s.Course.CType),
                    SectionNum = i + 1,
                    InstructorName = s.Instructor.IName,
                    StartTime = s.STime,
                    EndTime = s.FTime,
                    Days = string.Join("/", s.DayGroupSections.Select(d => d.Day)),
                    Hours = s.Course.CHrs,
                }).ToList()
            };
    }
}