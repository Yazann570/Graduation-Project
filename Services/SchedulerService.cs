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
        Task<List<FavouriteDto>> GetAllFavouritesAsync(string studentId);
        Task<bool> ToggleFavouriteAsync(ToggleFavouriteRequest req, string studentId);
        Task<bool> RemoveFavouriteByIdAsync(int favId, string studentId);
        Task<List<FilterDto>> GetAllFiltersAsync(string studentId);
        Task<List<SelectedCourseDto>> GetSelectedCoursesAsync(string studentId);
        Task AddCourseAsync(string studentId, AddCourseRequest req);
        Task RemoveCourseAsync(string studentId, string courseId);
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

        // ── Helper: execute raw SQL via direct ADO.NET (bypasses EF Core transaction) ──
        private async Task ExecSql(string sql, params object[] parameters)
        {
            var conn = _db.Database.GetDbConnection();
            bool weOpenedIt = conn.State == System.Data.ConnectionState.Closed;
            if (weOpenedIt) await conn.OpenAsync();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                for (int i = 0; i < parameters.Length; i++)
                {
                    var p = cmd.CreateParameter();
                    p.ParameterName = "p" + i;
                    p.Value = parameters[i];
                    cmd.Parameters.Add(p);
                }
                // Replace {0},{1},... with :p0,:p1,... (Oracle named params)
                for (int i = parameters.Length - 1; i >= 0; i--)
                    cmd.CommandText = cmd.CommandText.Replace("{" + i + "}", ":p" + i);

                await cmd.ExecuteNonQueryAsync();
            }
            finally
            {
                if (weOpenedIt) await conn.CloseAsync();
            }
        }
        private static bool IsNoInstructorRequiredCourse(Course course)
        {
            return course.CId == "11493"
                || course.CId == "11494"
                || course.CId == "11391";
        }
        private static string FormatDays(Section s)
        {
            var dayOrder = new Dictionary<string, int>
                {
                    { "Sun", 1 },
                    { "Mon", 2 },
                    { "Tue", 3 },
                    { "Wed", 4 },
                    { "Thu", 5 }
                };

            return string.Join("/",
                s.DayGroupSections
                    .Select(d => d.Day)
                    .Distinct()
                    .OrderBy(d => dayOrder.GetValueOrDefault(d, 99))
            );
        }

        // ── 1. Remaining courses ─────────────────────────────
        // Reads from STUDENT_REMAINING_COURSE (per-student list) minus
        // anything already added to STUDENT_COURSE (selected for scheduling)
        public async Task<List<RemainingCourseDto>> GetRemainingCoursesAsync(string studentId)
        {
            // Courses this student has already added to the scheduler
            var selectedIds = await _db.StudentCourses
                .Where(sc => sc.StId == studentId)
                .Select(sc => sc.CId)
                .ToListAsync();

            // Courses assigned to this student from STUDENT_REMAINING_COURSE
            var remainingIds = await _db.StudentRemainingCourses
                .Where(sr => sr.StId == studentId)
                .Select(sr => sr.CId)
                .ToListAsync();

            // Only show courses that are in student's remaining list AND not yet selected
            var toShow = remainingIds.Except(selectedIds).ToList();

            if (toShow.Count == 0) return new List<RemainingCourseDto>();

            var courses = await _db.Courses
                .Include(c => c.Sections).ThenInclude(s => s.Instructor)
                .Where(c => toShow.Contains(c.CId))
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
            "INSERT INTO FILTER (F_ID, ST_ID, S_TIME, F_TIME, MIN_BREAK, MAX_BREAK, CREDIT_HOURS) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})",
            filterId, studentId, req.StartTime, req.EndTime,
            req.MinBreak, req.MaxBreak, req.CreditHours);

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
                CreditHours = req.CreditHours,
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
            var maxCreditHours = filter.CreditHours;

            var prefs = await _db.InstructorsAdded
                .Where(ia => ia.StId == studentId)
                .GroupBy(ia => ia.CId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(ia => ia.IId).ToHashSet());

            var selectedCourseIds = await _db.StudentCourses
            .Where(sc => sc.StId == studentId)
            .Select(sc => sc.CId)
            .ToListAsync();

            var noInstructorCourseIds = await _db.Courses
                .Where(c => selectedCourseIds.Contains(c.CId))
                .ToListAsync();

            var schedulingCourseIds = selectedCourseIds;

            //if (schedulingCourseIds.Count == 0 && alwaysIncludedCourseIds.Count == 0)
            //    return new List<ScheduleResultDto>();

            var allSections = await _db.Sections
                .Include(s => s.Course)
                .Include(s => s.Instructor)
                .Include(s => s.DayGroupSections)
                .Where(s => schedulingCourseIds.Contains(s.CId))
                .ToListAsync();
            foreach (var s in allSections)
            {
                Console.WriteLine($"Course: {s.Course.CName}, IsOnline: [{s.Course.IsOnline}]");
            }
            foreach (var c in noInstructorCourseIds)
            {
                Console.WriteLine($"Course ID: {c.CId}, Course Name: {c.CName}");
            }
            var candidatesByCourse = allSections
                .Where(s =>
                {
                    if (prefs.TryGetValue(s.CId, out var ids) && !ids.Contains(s.IId)) return false;
                    var secDays = s.DayGroupSections.Select(d => d.Day).ToHashSet();

                    // Blended sections (IS_ONLINE='B') have physical days AND online days.
                    // For blended: accept if at least one day falls in the student's allowed days.
                    // For all others: all section days must be within allowed days.
                    bool isBlended = s.Course.IsOnline == "B";
                    if (isBlended)
                    {
                        if (!secDays.Intersect(allowedDays).Any()) return false;
                    }
                    else
                    {
                        if (!secDays.IsSubsetOf(allowedDays)) return false;
                    }

                    var start = TimeOnly.Parse(s.STime);
                    var end = TimeOnly.Parse(s.FTime);
                    return start >= filterStart && end <= filterEnd;
                })
                .GroupBy(s => s.CId)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (candidatesByCourse.Count < prefs.Count)
                return new List<ScheduleResultDto>();

            var results = new List<List<Section>>();

            var availableCourseGroups = candidatesByCourse
                .Select(kvp => new CourseGroup
                {
                    CourseId = kvp.Key,
                    Sections = kvp.Value,
                    CreditHours = kvp.Value.First().Course.CHrs
                })
                .ToList();

            var courseSubsets = GenerateCourseSubsets(
                availableCourseGroups,
                maxCreditHours
            );

            foreach (var subset in courseSubsets)
            {
                var courseList = subset
                    .Select(x => x.Sections)
                    .ToList();

                Backtrack(
                    courseList,
                    0,
                    new List<Section>(),
                    filter,
                    allowedDays,
                    results,
                    maxResults: 100
                );
            }
            
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
                var totalHours = combo.Sum(s => s.Course.CHrs);
                if (totalHours != maxCreditHours)
                    continue;
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
                var dto = MapSchedule(sched, combo, isFav: false);

                

                dtos.Add(dto);
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

        // ── 4c. Remove favourite by FAV_ID ───────────────────
        public async Task<bool> RemoveFavouriteByIdAsync(int favId, string studentId)
        {
            // Verify it belongs to this student before deleting
            var fav = await _db.Favourites
                .Include(f => f.Filter)
                .FirstOrDefaultAsync(f => f.FavId == favId && f.Filter.StId == studentId);

            if (fav == null) return false;

            await ExecSql("DELETE FROM FAVOURITE WHERE FAV_ID = {0}", favId);
            return true;
        }

        // ── 4b. Get ALL favourites across all filters ────────
        public async Task<List<FavouriteDto>> GetAllFavouritesAsync(string studentId)
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
                .Where(f => f.Filter.StId == studentId)
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

        // ── 6. Get selected courses ──────────────────────
        public async Task<List<SelectedCourseDto>> GetSelectedCoursesAsync(string studentId)
        {
            // Load all courses in STUDENT_COURSE for this student
            var selectedIds = await _db.StudentCourses
                .Where(sc => sc.StId == studentId)
                .Select(sc => sc.CId)
                .ToListAsync();

            if (selectedIds.Count == 0) return new List<SelectedCourseDto>();

            // Load course details with all available sections/instructors
            var courses = await _db.Courses
                .Include(c => c.Sections).ThenInclude(s => s.Instructor)
                .Where(c => selectedIds.Contains(c.CId))
                .ToListAsync();

            // Load which instructors this student has chosen per course
            var prefs = await _db.InstructorsAdded
                .Include(ia => ia.Instructor)
                .Where(ia => ia.StId == studentId && selectedIds.Contains(ia.CId))
                .ToListAsync();

            var prefsByCourse = prefs
                .GroupBy(ia => ia.CId)
                .ToDictionary(g => g.Key, g => g.Select(ia => new InstructorSummaryDto
                { IId = ia.IId, IName = ia.Instructor.IName }).ToList());

            return courses.Select(c => new SelectedCourseDto
            {
                CId = c.CId,
                CName = c.CName,
                CHrs = c.CHrs,
                CType = c.CType,
                RequirementLabel = RequirementLabels.GetValueOrDefault(c.CType, c.CType),
                IsOnline = c.IsOnline,
                SelectedInstructors = prefsByCourse.GetValueOrDefault(c.CId, new()),
                AvailableInstructors = c.Sections
                    .Select(s => s.Instructor)
                    .DistinctBy(i => i.IId)
                    .Select(i => new InstructorSummaryDto { IId = i.IId, IName = i.IName })
                    .ToList(),
            }).ToList();
        }

        // ── 7. Add course ─────────────────────────────────────
        public async Task AddCourseAsync(string studentId, AddCourseRequest req)
        {
            // Use direct ADO.NET — ExecuteSqlRawAsync doesn't auto-commit in Oracle
            bool exists = await _db.StudentCourses
                .AnyAsync(sc => sc.StId == studentId && sc.CId == req.CourseId);

            if (!exists)
                await ExecSql(
                    "INSERT INTO STUDENT_COURSE (ST_ID, C_ID) VALUES ({0}, {1})",
                    studentId, req.CourseId);

            // Replace instructor preferences for this course
            await ExecSql(
                "DELETE FROM INSTRUCTOR_ADDED WHERE ST_ID = {0} AND C_ID = {1}",
                studentId, req.CourseId);

            foreach (var iId in req.InstructorIds)
                await ExecSql(
                    "INSERT INTO INSTRUCTOR_ADDED (I_ID, C_ID, ST_ID) VALUES ({0}, {1}, {2})",
                    iId, req.CourseId, studentId);
        }

        // ── 8. Remove course ──────────────────────────────────
        public async Task RemoveCourseAsync(string studentId, string courseId)
        {
            await ExecSql(
                "DELETE FROM INSTRUCTOR_ADDED WHERE ST_ID = {0} AND C_ID = {1}",
                studentId, courseId);

            await ExecSql(
                "DELETE FROM STUDENT_COURSE WHERE ST_ID = {0} AND C_ID = {1}",
                studentId, courseId);
        }

        // ── 9. Get all filters (debug) ───────────────────────
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

        private class CourseGroup
        {
            public string CourseId { get; set; } = null!;
            public List<Section> Sections { get; set; } = new();
            public int CreditHours { get; set; }
        }

        private static List<List<CourseGroup>> GenerateCourseSubsets(
            List<CourseGroup> courses,
            int maxCreditHours)
        {

            var results = new List<List<CourseGroup>>();

            void BacktrackSubsets(int index, List<CourseGroup> current, int currentHours)
            {
                if (currentHours > maxCreditHours)
                    return;

                if (index == courses.Count)
                {
                    if (current.Count > 0 && currentHours == maxCreditHours)
                        results.Add(new List<CourseGroup>(current));

                    return;
                }

                BacktrackSubsets(index + 1, current, currentHours);

                current.Add(courses[index]);
                BacktrackSubsets(index + 1, current, currentHours + courses[index].CreditHours);
                current.RemoveAt(current.Count - 1);
            }

            BacktrackSubsets(0, new List<CourseGroup>(), 0);

            return results
                .OrderByDescending(subset => subset.Sum(c => c.CreditHours))
                .ToList();
        }

        private static void Backtrack(
    List<List<Section>> byCourse, int idx,
    List<Section> current, Filter filter,
    HashSet<string> allowedDays,
    List<List<Section>> results, int maxResults)
        {
            if (results.Count >= maxResults) return;

            if (idx == byCourse.Count)
            {
                // All courses placed — now validate break constraints on the full schedule
                if (IsBreakValid(current, filter.MinBreak, filter.MaxBreak, allowedDays))
                    results.Add(new List<Section>(current));
                return;
            }

            foreach (var sec in byCourse[idx])
            {
                // Only check overlap here — breaks are checked after all courses are placed
                if (!HasOverlap(sec, current, allowedDays))
                {
                    current.Add(sec);
                    Backtrack(byCourse, idx + 1, current, filter, allowedDays, results, maxResults);
                    current.RemoveAt(current.Count - 1);
                }
            }
        }

        /// <summary>
        /// Checks whether <paramref name="candidate"/> conflicts with any already-chosen section.
        ///
        /// Rules:
        /// 1. Time overlap on any shared effective day → always a conflict.
        /// 2. Min/max break is checked ONLY between ADJACENT sections on the same day
        ///    (the immediately preceding and following class).
        ///    Checking all pairs was wrong: a 09-10 class and a 13-14 class with two
        ///    classes between them should NOT be compared for break purposes.
        /// 3. Blended sections (IS_ONLINE='B'): only days within allowedDays matter.
        /// </summary>
        private static bool HasOverlap(
    Section candidate, List<Section> chosen,
    HashSet<string> allowedDays)
        {
            var candEffective = candidate.DayGroupSections
                .Select(d => d.Day)
                .Where(d => allowedDays.Contains(d))
                .ToHashSet();

            if (!candEffective.Any()) return false;

            var candStart = TimeOnly.Parse(candidate.STime);
            var candEnd = TimeOnly.Parse(candidate.FTime);

            foreach (var day in candEffective)
            {
                var onThisDay = chosen
                    .Where(s => s.DayGroupSections
                        .Select(d => d.Day)
                        .Where(d => allowedDays.Contains(d))
                        .Contains(day))
                    .ToList();

                foreach (var sec in onThisDay)
                {
                    var secStart = TimeOnly.Parse(sec.STime);
                    var secEnd = TimeOnly.Parse(sec.FTime);
                    if (candStart < secEnd && candEnd > secStart) return true;
                    Console.WriteLine(sec.Course.IsOnline.ToString());
                }
            }

            return false;
        }

        // Validates break constraints over a COMPLETE schedule combination.
        // Called only when all courses have been placed.
        private static bool IsBreakValid(
            List<Section> sections, int minBreak, int maxBreak,
            HashSet<string> allowedDays)
        {
            var allDays = sections
                .SelectMany(s => s.DayGroupSections
                    .Select(d => d.Day)
                    .Where(d => allowedDays.Contains(d)))
                .Distinct();

            foreach (var day in allDays)
            {
                // All sections physically on this day, sorted by start time
                var onThisDay = sections
                    .Where(s => s.DayGroupSections
                        .Select(d => d.Day)
                        .Where(d => allowedDays.Contains(d))
                        .Contains(day))
                    .OrderBy(s => TimeOnly.Parse(s.STime))
                    .ToList();

                // Check every adjacent pair on this day
                for (int i = 0; i < onThisDay.Count - 1; i++)
                {
                    int gap = (int)(TimeOnly.Parse(onThisDay[i + 1].STime)
                                  - TimeOnly.Parse(onThisDay[i].FTime)).TotalMinutes;

                    if (gap < minBreak || gap > maxBreak) return false;
                }
            }

            return true;
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
                    SectionNum = s.SectionNo ?? 0,
                    InstructorName = s.Instructor.IName,
                    StartTime = s.STime,
                    EndTime = s.FTime,
                    Days = FormatDays(s),
                    Hours = s.Course.CHrs,
                    IsOnline = s.Course.IsOnline,
                }).ToList(),

            };
            
    }
}