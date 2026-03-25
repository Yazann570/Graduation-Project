// ============================================================
//  API LAYER  — no auth, single-user system
// ============================================================

const API_BASE = '/api';

async function apiFetch(path, options = {}) {
    let res;
    try {
        res = await fetch(API_BASE + path, {
            headers: { 'Content-Type': 'application/json', ...(options.headers || {}) },
            ...options,
        });
    } catch (networkErr) {
        throw new Error(
            'Cannot reach the server. Make sure the ASP.NET app is running.\n(' + networkErr.message + ')'
        );
    }

    const contentType = res.headers.get('content-type') || '';
    if (!contentType.includes('application/json')) {
        const preview = (await res.text()).slice(0, 300);
        throw new Error(
            'API "' + path + '" returned HTTP ' + res.status + ' — not JSON.\n' +
            'Likely: route not registered or app startup error.\nResponse: ' + preview
        );
    }

    const json = await res.json();
    if (!res.ok || !json.Success) {
        throw new Error(json.Message || 'HTTP ' + res.status);
    }
    return json.Data;
}

async function apiGetStudentInfo() {
    return apiFetch('/student/me');
}

async function apiGetCurrentSchedule() {
    return apiFetch('/student/current-schedule');
}

async function apiGetRemainingCourses() {
    const courses = await apiFetch('/courses/remaining');
    if (!courses) return [];

    const typeMap = {
        CP: 'compulsory-program',
        CS: 'compulsory-school',
        CU: 'compulsory-university',
        EU: 'elective-university',
        EP: 'elective-program',
    };

    return courses.map(c => ({
        id: c.CId,
        courseNumber: c.CId,
        name: c.CName,
        hours: c.CHrs,
        requirementType: typeMap[c.CType] || c.CType,
        availableInstructors: c.AvailableInstructors.map(i => i.IName),
        _instructorObjects: c.AvailableInstructors,
    }));
}

async function apiSaveFilter({ startTime, endTime, minBreak, maxBreak, days, courseInstructors }) {
    return apiFetch('/filter', {
        method: 'POST',
        body: JSON.stringify({
            StartTime: startTime, EndTime: endTime,
            MinBreak: minBreak, MaxBreak: maxBreak,
            Days: days, CourseInstructors: courseInstructors,
        }),
    });
}

async function apiGenerateSchedules(filterId) {
    const schedules = await apiFetch('/scheduler/generate', {
        method: 'POST',
        body: JSON.stringify({ FilterId: filterId }),
    });
    if (!schedules) return [];

    const typeMap = {
        CP: 'compulsory-program',
        CS: 'compulsory-school',
        CU: 'compulsory-university',
        EU: 'elective-university',
        EP: 'elective-program',
    };

    return schedules.map(s => ({
        id: String(s.SchedId),
        totalHours: s.TotalHours,
        isFav: s.IsFavourite,
        courses: s.Courses.map(c => ({
            courseNumber: c.CourseNumber,
            name: c.CourseName,
            requirementType: typeMap[c.CType] || c.CType,
            section: c.SectionNum,
            instructor: c.InstructorName,
            day: c.Days,
            time: c.StartTime + '–' + c.EndTime,
        })),
    }));
}

async function apiGetFavourites(filterId) {
    return apiFetch('/favourite?filterId=' + filterId);
}

async function apiToggleFavourite(filterId, scheduleId) {
    return apiFetch('/favourite/toggle', {
        method: 'POST',
        body: JSON.stringify({ FilterId: filterId, ScheduleId: parseInt(scheduleId) }),
    });
}

// ============================================================
//  UI CONSTANTS  (pure frontend, not from DB)
// ============================================================
const REQUIREMENT_TYPES = {
    'compulsory-program': { label: 'Compulsory Program Requirements', cssClass: 'req-compulsory-program' },
    'compulsory-school': { label: 'Compulsory School Requirements', cssClass: 'req-compulsory-school' },
    'compulsory-university': { label: 'Compulsory University Requirements', cssClass: 'req-compulsory-university' },
    'elective-university': { label: 'Elective University Requirements', cssClass: 'req-elective-university' },
    'elective-program': { label: 'Elective Program Requirements', cssClass: 'req-elective-program' },
};

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday'];

// ============================================================
//  STATE
// ============================================================
let REMAINING_COURSES = [];   // loaded from API on init

let state = {
    activeTab: 'smart-scheduler',
    currentFilterId: null,          // set after saveFilter succeeds
    currentScheduleCourses: null,          // lazy-loaded when tab opened
    studentInfo: null,          // lazy-loaded when tab opened
    // smart scheduler
    selectedInstructors: {},            // { courseId: [instructorName, ...] }
    openDropdown: null,
    selectedCourses: [],
    selectedDays: [],
    timeRange: { start: '08:00', end: '17:00' },
    breakHours: { min: 0, max: 2 },
    creditHours: 15,
    generatedSchedules: [],
    currentScheduleIndex: 0,
    favoriteIds: [],
    noSchedulesFound: false,
    isLoading: false,
};

// ============================================================
//  LOADING OVERLAY
// ============================================================
function showLoading(msg) {
    let el = document.getElementById('loading-overlay');
    if (!el) {
        el = document.createElement('div');
        el.id = 'loading-overlay';
        el.className = 'loading-overlay';
        document.body.appendChild(el);
    }
    el.innerHTML = `<div class="spinner"></div><span>${msg || 'Loading…'}</span>`;
    el.style.display = 'flex';
}
function hideLoading() {
    const el = document.getElementById('loading-overlay');
    if (el) el.style.display = 'none';
}

// ============================================================
//  INIT  (replaces bare render() call at the end)
// ============================================================
async function init() {
    showLoading('Loading courses…');
    try {
        REMAINING_COURSES = await apiGetRemainingCourses();
    } catch (e) {
        console.error('Could not load remaining courses:', e);
        REMAINING_COURSES = [];
    }
    hideLoading();
    render();
}

// ============================================================
//  NAVIGATION
// ============================================================
async function navigate(tab) {
    state.activeTab = tab;
    document.querySelectorAll('.sidebar-item').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.sidebar-item').forEach(btn => {
        if (btn.getAttribute('onclick') && btn.getAttribute('onclick').includes("'" + tab + "'"))
            btn.classList.add('active');
    });

    // Lazy-load current schedule
    if (tab === 'current-schedule' && !state.currentScheduleCourses) {
        showLoading('Loading schedule…');
        try {
            state.currentScheduleCourses = await apiGetCurrentSchedule();
        } catch (e) {
            console.error('Could not load current schedule:', e);
            state.currentScheduleCourses = [];
        }
        hideLoading();
    }

    // Lazy-load student info
    if (tab === 'student-info' && !state.studentInfo) {
        showLoading('Loading student info…');
        try {
            state.studentInfo = await apiGetStudentInfo();
        } catch (e) {
            console.error('Could not load student info:', e);
            state.studentInfo = { StId: 'N/A', Email: 'N/A', Phone: 'N/A' };
        }
        hideLoading();
    }

    render();
}

// ============================================================
//  RENDER DISPATCHER
// ============================================================
function render() {
    const area = document.getElementById('content-area');
    if (state.activeTab === 'smart-scheduler') {
        area.innerHTML = renderSmartScheduler();
        bindSmartScheduler();
    } else if (state.activeTab === 'current-schedule') {
        area.innerHTML = renderCurrentSchedule();
    } else if (state.activeTab === 'student-info') {
        area.innerHTML = renderStudentInfo();
    } else {
        const label = state.activeTab.split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
        area.innerHTML = `<div class="under-dev"><h1>${label}</h1><p>This section is under development.</p></div>`;
    }
}

// ============================================================
//  SMART SCHEDULER PAGE
// ============================================================
function renderSmartScheduler() {
    return `
    <h1 class="page-title">Smart Scheduler</h1>

    <!-- Remaining Courses -->
    <div class="section-mb">
      <h2 class="section-title">Remaining Courses</h2>
      ${REMAINING_COURSES.length === 0
            ? `<p style="color:#666;padding:16px 0;">No remaining courses found.</p>`
            : `
      <div class="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Course Number</th><th>Course Title</th><th>Course Hours</th>
              <th>Requirement Type</th><th>Instructor</th><th>Action</th>
            </tr>
          </thead>
          <tbody>
            ${REMAINING_COURSES.map(course => {
                const rt = REQUIREMENT_TYPES[course.requirementType] || { label: course.requirementType, cssClass: '' };
                const selInstr = state.selectedInstructors[course.id] || [];
                const label = selInstr.length > 0 ? selInstr.length + ' selected' : 'Select Instructors';
                return `
                <tr class="${rt.cssClass}">
                  <td>${course.courseNumber}</td>
                  <td>${course.name}</td>
                  <td>${course.hours}</td>
                  <td>${rt.label}</td>
                  <td>
                    <div class="dropdown-container" id="dc-${course.id}">
                      <button class="dropdown-trigger" onclick="toggleDropdown('${course.id}', event)">
                        <span id="dt-label-${course.id}">${label}</span>
                        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <polyline points="6 9 12 15 18 9"/>
                        </svg>
                      </button>
                      <div class="dropdown-menu ${state.openDropdown === course.id ? 'open' : ''}" id="dm-${course.id}">
                        ${course.availableInstructors.map(instr => `
                          <label>
                            <input type="checkbox" ${selInstr.includes(instr) ? 'checked' : ''}
                              onchange="toggleInstructor('${course.id}', '${instr}')" />
                            ${instr}
                          </label>
                        `).join('')}
                      </div>
                    </div>
                  </td>
                  <td>
                    <button class="btn btn-primary" onclick="handleAddCourse('${course.id}')">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                      </svg>
                      Add Course
                    </button>
                  </td>
                </tr>
              `;
            }).join('')}
          </tbody>
        </table>
      </div>`
        }

      <div class="important-note">
        <svg viewBox="0 0 20 20" fill="currentColor">
          <path fill-rule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7-4a1 1 0 11-2 0 1 1 0 012 0zM9 9a1 1 0 000 2v3a1 1 0 001 1h1a1 1 0 100-2v-3a1 1 0 00-1-1H9z" clip-rule="evenodd"/>
        </svg>
        <p>
          <strong>Important Note:</strong> Make sure to add the courses in
          <span style="font-weight:600;color:#001f3f;">dark blue</span>
          (Compulsory Program Requirements) as they're the backbone courses in the major.
          Not registering them in the considered time might cause graduation delay issues for you.
        </p>
      </div>
    </div>

    <!-- Selected Courses -->
    ${state.selectedCourses.length > 0 ? `
    <div class="section-mb">
      <h2 class="section-title">Selected Courses</h2>
      <div class="table-wrapper">
        <table>
          <thead>
            <tr>
              <th>Course Number</th><th>Course Title</th><th>Course Hours</th>
              <th>Requirement Type</th><th>Instructors</th><th>Action</th>
            </tr>
          </thead>
          <tbody>
            ${state.selectedCourses.map(course => {
            const rt = REQUIREMENT_TYPES[course.requirementType] || { label: course.requirementType, cssClass: '' };
            return `
                <tr class="${rt.cssClass}">
                  <td>${course.courseNumber}</td>
                  <td>${course.name}</td>
                  <td>${course.hours}</td>
                  <td>${rt.label}</td>
                  <td>
                    <select class="inline-select">
                      ${course.instructors.map(instr => `<option value="${instr}">${instr}</option>`).join('')}
                    </select>
                  </td>
                  <td>
                    <button class="btn btn-danger" onclick="handleRemoveCourse('${course.id}')">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="3 6 5 6 21 6"/>
                        <path d="M19 6l-1 14a2 2 0 01-2 2H8a2 2 0 01-2-2L5 6"/>
                        <path d="M10 11v6M14 11v6"/><path d="M9 6V4h6v2"/>
                      </svg>
                      Remove
                    </button>
                  </td>
                </tr>
              `;
        }).join('')}
          </tbody>
        </table>
      </div>
    </div>
    ` : ''}

    <!-- Filters -->
    <div class="filters-box section-mb">
      <h2 class="section-title" style="margin-bottom:16px;">Preferences &amp; Filters</h2>

      <div class="filters-row">
        <div class="subsection-title">Preferred Days</div>
        <div class="days-grid">
          ${WEEKDAYS.map(day => `
            <label>
              <input type="checkbox" ${state.selectedDays.includes(day) ? 'checked' : ''}
                onchange="toggleDay('${day}')" />
              ${day}
            </label>
          `).join('')}
        </div>
      </div>

      <div class="filters-row">
        <div class="subsection-title">Time Range</div>
        <div class="time-row">
          <div class="field">
            <label>From:</label>
            <input type="time" value="${state.timeRange.start}"
              onchange="updateTimeRange('start', this.value)" />
          </div>
          <div class="field">
            <label>To:</label>
            <input type="time" value="${state.timeRange.end}"
              onchange="updateTimeRange('end', this.value)" />
          </div>
        </div>
      </div>

      <div class="filters-row">
        <div class="subsection-title">Break Hours</div>
        <div class="break-row">
          <div class="field">
            <label>Minimum:</label>
            <input type="number" min="0" max="5" value="${state.breakHours.min}"
              onchange="updateBreakHours('min', this.value)" />
            <span style="font-size:13px;color:#555;">hours</span>
          </div>
          <div class="field">
            <label>Maximum:</label>
            <input type="number" min="0" max="5" value="${state.breakHours.max}"
              onchange="updateBreakHours('max', this.value)" />
            <span style="font-size:13px;color:#555;">hours</span>
          </div>
        </div>
      </div>

      <div class="filters-row" style="margin-bottom:24px;">
        <div class="subsection-title">Credit Hours</div>
        <div class="credit-row">
          <input type="number" class="credit-input" min="3" max="21" value="${state.creditHours}"
            onchange="state.creditHours = parseInt(this.value) || 0" />
          <span>credit hours</span>
        </div>
      </div>

      <button class="btn btn-primary btn-large" onclick="generateSchedules()">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="3" y="4" width="18" height="18" rx="2"/>
          <line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/>
          <line x1="3" y1="10" x2="21" y2="10"/>
        </svg>
        Generate Schedules
      </button>
    </div>

    <!-- Debug: Saved Filters -->
    ${renderSavedFiltersDebug()}

    <!-- Generated Schedules -->
    ${state.generatedSchedules.length > 0 ? renderGeneratedSchedules() : ''}

    <!-- Favorite Schedules -->
    ${state.generatedSchedules.filter(s => state.favoriteIds.includes(s.id)).length > 0
            ? renderFavoriteSchedules() : ''}

    <!-- No Schedules Found -->
    ${state.noSchedulesFound ? `
    <div class="no-schedules section-mb">
      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round"
          d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z"/>
      </svg>
      <h2>No Schedules Found</h2>
      <p>We couldn't generate any schedules that match your current filters and preferences.</p>
      <p class="italic">Please adjust your filters and try generating schedules again. We're here to help you find the perfect schedule! 🎓</p>
    </div>
    ` : ''}
  `;
}

// ── Generated Schedules ──────────────────────────────────────
function renderGeneratedSchedules() {
    const schedule = state.generatedSchedules[state.currentScheduleIndex];
    const isFav = state.favoriteIds.includes(schedule.id);
    const total = state.generatedSchedules.length;
    const idx = state.currentScheduleIndex;

    return `
    <div class="section-mb">
      <h2 class="section-title">Generated Schedules</h2>
      <div class="schedule-box">
        <div class="schedule-nav">
          <button class="btn btn-primary" onclick="goToPrevSchedule()" ${idx === 0 ? 'disabled' : ''}>
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="15 18 9 12 15 6"/>
            </svg>
            Previous
          </button>
          <div class="schedule-nav-center">
            <h3>Schedule Option ${idx + 1} of ${total}</h3>
            <p>Total Hours: ${schedule.totalHours}</p>
          </div>
          <button class="btn btn-primary" onclick="goToNextSchedule()" ${idx === total - 1 ? 'disabled' : ''}>
            Next
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="9 18 15 12 9 6"/>
            </svg>
          </button>
        </div>

        <div class="table-wrapper" style="margin-bottom:16px;">
          <table>
            <thead>
              <tr>
                <th>Course Number</th><th>Course Title</th><th>Requirement Type</th>
                <th>Section</th><th>Instructor</th><th>Day</th><th>Time</th>
              </tr>
            </thead>
            <tbody>
              ${schedule.courses.map((c, i) => `
                <tr class="${i % 2 === 0 ? 'row-alt-0' : 'row-alt-1'}">
                  <td>${c.courseNumber}</td>
                  <td>${c.name}</td>
                  <td>${(REQUIREMENT_TYPES[c.requirementType] || { label: c.requirementType }).label}</td>
                  <td>${c.section}</td>
                  <td>${c.instructor}</td>
                  <td>${c.day}</td>
                  <td>${c.time}</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>

        <div class="schedule-actions">
          <button class="btn btn-large ${isFav ? 'btn-yellow' : 'btn-primary'}"
                  onclick="toggleFavorite('${schedule.id}')">
            <svg viewBox="0 0 24 24" stroke="currentColor" stroke-width="2" fill="${isFav ? 'white' : 'none'}">
              <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
            </svg>
            ${isFav ? 'Remove from Favorites' : 'Add to Favorites'}
          </button>
          <button class="btn btn-primary btn-large" onclick="handleExportToPDF('${schedule.id}')">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/>
              <polyline points="7 10 12 15 17 10"/>
              <line x1="12" y1="15" x2="12" y2="3"/>
            </svg>
            Export to PDF
          </button>
        </div>
      </div>
    </div>
  `;
}

// ── Favorite Schedules ───────────────────────────────────────
function renderFavoriteSchedules() {
    const favSchedules = state.generatedSchedules.filter(s => state.favoriteIds.includes(s.id));
    return `
    <div class="section-mb">
      <h2 class="section-title">Favorite Schedules</h2>
      ${favSchedules.map((schedule, si) => `
        <div class="fav-card">
          <div class="fav-card-header">
            <div class="fav-card-title">
              <svg class="star-icon-filled" viewBox="0 0 24 24" stroke="#fbbf24" stroke-width="1.5">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
              </svg>
              <h3>Favorite Schedule ${si + 1}</h3>
              <span class="total-hrs">Total Hours: ${schedule.totalHours}</span>
            </div>
            <div class="fav-card-actions">
              <button class="btn btn-primary" onclick="handleExportToPDF('${schedule.id}')">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4"/>
                  <polyline points="7 10 12 15 17 10"/>
                  <line x1="12" y1="15" x2="12" y2="3"/>
                </svg>
                Export to PDF
              </button>
              <button class="btn-remove-link" onclick="toggleFavorite('${schedule.id}')">Remove</button>
            </div>
          </div>
          <div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Course Number</th><th>Course Title</th><th>Requirement Type</th>
                  <th>Section</th><th>Instructor</th><th>Day</th><th>Time</th>
                </tr>
              </thead>
              <tbody>
                ${schedule.courses.map((c, i) => `
                  <tr class="${i % 2 === 0 ? 'row-alt-0' : 'row-alt-1'}">
                    <td>${c.courseNumber}</td>
                    <td>${c.name}</td>
                    <td>${(REQUIREMENT_TYPES[c.requirementType] || { label: c.requirementType }).label}</td>
                    <td>${c.section}</td>
                    <td>${c.instructor}</td>
                    <td>${c.day}</td>
                    <td>${c.time}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>
        </div>
      `).join('')}
    </div>
  `;
}

// ============================================================
//  CURRENT SCHEDULE PAGE
// ============================================================
function renderCurrentSchedule() {
    const courses = state.currentScheduleCourses || [];

    function dotClass(absences) {
        if (absences <= 4) return 'dot-green';
        if (absences === 5) return 'dot-yellow';
        return 'dot-red';
    }

    function onlineLabel(code) {
        if (code === 'Y') return 'Online';
        if (code === 'B') return 'Blended';
        return 'No';
    }

    return `
    <h1 class="page-title">Current Schedule</h1>
    <div class="legend">
      <div class="legend-item"><div class="dot dot-green"></div> Regular (less than 4 absences)</div>
      <div class="legend-item"><div class="dot dot-yellow"></div> Under warning</div>
      <div class="legend-item"><div class="dot dot-red"></div> Under threat of denial</div>
      <div class="legend-item"><div class="dot dot-gray"></div> Denied</div>
    </div>
    <div class="table-wrapper">
      <table>
        <thead>
          <tr>
            <th>Course Number</th><th>Course Title</th><th>Course Hours</th>
            <th>Section</th><th>Instructor</th><th>Classroom</th>
            <th>Day</th><th>Time</th><th>Online</th>
            <th>Number of absences</th><th>Course procedures</th>
          </tr>
        </thead>
        <tbody>
          ${courses.length === 0
            ? `<tr><td colspan="11" style="text-align:center;color:#666;padding:24px;">No enrolled courses found.</td></tr>`
            : courses.map((c, i) => `
              <tr class="${i % 2 === 0 ? 'row-alt-0' : 'row-alt-1'}">
                <td>${c.CourseNumber}</td>
                <td style="color:#09618a;">${c.Title}</td>
                <td>${c.Hours}</td>
                <td>${c.SectionNum}</td>
                <td>${c.Instructor}</td>
                <td>${c.Classroom}</td>
                <td>${c.Days}</td>
                <td>${c.StartTime}–${c.EndTime}</td>
                <td>${onlineLabel(c.IsOnline)}</td>
                <td>
                  <div style="display:flex;align-items:center;gap:8px;">
                    <div class="dot ${dotClass(c.Absences)}"></div>
                    <span>${c.Absences}</span>
                  </div>
                </td>
                <td>
                  <div style="display:flex;gap:8px;align-items:center;">
                    <span style="color:#2563eb;cursor:pointer;">Evaluation</span>
                    <span>|</span>
                    <span style="color:#2563eb;cursor:pointer;">Note to instructor</span>
                    <span>|</span>
                    <span style="color:#2563eb;cursor:pointer;">⬇</span>
                  </div>
                </td>
              </tr>
            `).join('')
        }
        </tbody>
      </table>
    </div>
    <div class="print-row">
      <button class="btn btn-primary btn-large" style="padding:12px 32px;" onclick="window.print()">
        Print according to time
      </button>
    </div>
  `;
}

// ============================================================
//  STUDENT INFO PAGE
// ============================================================
function renderStudentInfo() {
    const s = state.studentInfo;
    if (!s) return `<div class="under-dev"><h1>Student Information</h1><p>Loading…</p></div>`;
    return `
    <h1 class="page-title">Student Information</h1>
    <div style="border:1px solid #e0e0e0;border-radius:8px;padding:24px;max-width:500px;">
      <table>
        <tbody>
          <tr><td style="padding:10px 16px;color:#555;width:160px;">Student ID</td>
              <td style="padding:10px 16px;font-weight:600;">${s.StId}</td></tr>
          <tr style="background:#f8f9fa;"><td style="padding:10px 16px;color:#555;">Email</td>
              <td style="padding:10px 16px;">${s.Email}</td></tr>
          <tr><td style="padding:10px 16px;color:#555;">Phone</td>
              <td style="padding:10px 16px;">${s.Phone || '—'}</td></tr>
        </tbody>
      </table>
    </div>
  `;
}

// ============================================================
//  BIND EVENTS
// ============================================================
function bindSmartScheduler() {
    document.addEventListener('click', handleOutsideClick, { once: false });
}

function handleOutsideClick(e) {
    if (!e.target.closest('.dropdown-container')) {
        if (state.openDropdown !== null) {
            state.openDropdown = null;
            document.querySelectorAll('.dropdown-menu').forEach(m => m.classList.remove('open'));
        }
    }
}

// ============================================================
//  SMART SCHEDULER ACTIONS
// ============================================================
function toggleDropdown(courseId, event) {
    event.stopPropagation();
    if (state.openDropdown === courseId) {
        state.openDropdown = null;
        document.getElementById('dm-' + courseId).classList.remove('open');
    } else {
        if (state.openDropdown) {
            const prev = document.getElementById('dm-' + state.openDropdown);
            if (prev) prev.classList.remove('open');
        }
        state.openDropdown = courseId;
        document.getElementById('dm-' + courseId).classList.add('open');
    }
}

function toggleInstructor(courseId, instructor) {
    const current = state.selectedInstructors[courseId] || [];
    if (current.includes(instructor)) {
        state.selectedInstructors[courseId] = current.filter(i => i !== instructor);
    } else {
        state.selectedInstructors[courseId] = [...current, instructor];
    }
    const labelEl = document.getElementById('dt-label-' + courseId);
    if (labelEl) {
        const sel = state.selectedInstructors[courseId] || [];
        labelEl.textContent = sel.length > 0 ? sel.length + ' selected' : 'Select Instructors';
    }
}

function handleAddCourse(courseId) {
    const course = REMAINING_COURSES.find(c => c.id === courseId);
    if (!course) return;
    const instructors = state.selectedInstructors[courseId] || [];
    if (instructors.length === 0) { alert('Please select at least one instructor first'); return; }
    if (state.selectedCourses.find(c => c.id === courseId)) { alert('Course already added'); return; }
    state.selectedCourses = [...state.selectedCourses, { ...course, instructors }];
    state.openDropdown = null;
    render();
}

function handleRemoveCourse(courseId) {
    state.selectedCourses = state.selectedCourses.filter(c => c.id !== courseId);
    render();
}

function toggleDay(day) {
    if (state.selectedDays.includes(day)) {
        state.selectedDays = state.selectedDays.filter(d => d !== day);
    } else {
        state.selectedDays = [...state.selectedDays, day];
    }
}

function updateTimeRange(key, value) {
    state.timeRange[key] = value;
}

function updateBreakHours(key, value) {
    state.breakHours[key] = parseInt(value) || 0;
}

function goToPrevSchedule() {
    if (state.currentScheduleIndex > 0) { state.currentScheduleIndex--; render(); }
}

function goToNextSchedule() {
    if (state.currentScheduleIndex < state.generatedSchedules.length - 1) {
        state.currentScheduleIndex++; render();
    }
}

function handleExportToPDF(scheduleId) {
    alert('Exporting Schedule ' + scheduleId + ' to PDF…\nThis would generate a PDF of the selected schedule.');
}

// ============================================================
//  DEBUG: SAVED FILTERS TABLE
// ============================================================
function renderSavedFiltersDebug() {
    const filters = state.debugFilters || [];
    return `
    <div style="margin-bottom:32px;">
      <div style="display:flex;align-items:center;gap:16px;margin-bottom:12px;">
        <h2 class="section-title" style="margin-bottom:0;">🛠 Saved Filters (Debug)</h2>
        <button class="btn btn-primary" onclick="loadDebugFilters()">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="1 4 1 10 7 10"/>
            <path d="M3.51 15a9 9 0 1 0 .49-3.54"/>
          </svg>
          Refresh from DB
        </button>
      </div>
      ${filters.length === 0
            ? '<p style="color:#666;font-size:13px;">No filters loaded yet — click Refresh to fetch from DB.</p>'
            : `<div class="table-wrapper">
            <table>
              <thead>
                <tr>
                  <th>Filter ID</th><th>Start Time</th><th>End Time</th>
                  <th>Min Break (min)</th><th>Max Break (min)</th><th>Days</th>
                </tr>
              </thead>
              <tbody>
                ${filters.map((f, i) => `
                  <tr class="${i % 2 === 0 ? 'row-alt-0' : 'row-alt-1'}">
                    <td><strong>${f.FId}</strong></td>
                    <td>${f.StartTime}</td>
                    <td>${f.EndTime}</td>
                    <td>${f.MinBreak}</td>
                    <td>${f.MaxBreak}</td>
                    <td>${(f.Days || []).join(', ') || '—'}</td>
                  </tr>
                `).join('')}
              </tbody>
            </table>
          </div>`
        }
    </div>
  `;
}

async function loadDebugFilters() {
    try {
        const filters = await apiFetch('/filter');
        state.debugFilters = filters || [];
    } catch (e) {
        alert('Could not load filters: ' + e.message);
        state.debugFilters = [];
    }
    render();
}

// ============================================================
//  GENERATE SCHEDULES  (async — calls the real API)
// ============================================================
async function generateSchedules() {
    if (state.selectedCourses.length === 0) {
        alert('Please add at least one course');
        return;
    }

    // Build courseInstructors: { courseId: [iId, iId, ...] }
    const courseInstructors = {};
    for (const course of state.selectedCourses) {
        const selectedNames = state.selectedInstructors[course.id] || course.instructors;
        const instrObjects = course._instructorObjects || [];
        courseInstructors[course.id] = instrObjects
            .filter(obj => selectedNames.includes(obj.IName))
            .map(obj => obj.IId);
    }

    // Day full name → 3-letter abbreviation for the API
    const dayAbbr = {
        Sunday: 'Sun', Monday: 'Mon', Tuesday: 'Tue', Wednesday: 'Wed', Thursday: 'Thu',
        Friday: 'Fri', Saturday: 'Sat'
    };
    const days = state.selectedDays.map(d => dayAbbr[d] || d);

    showLoading('Saving preferences…');

    try {
        // Step 1 — save filter
        const filter = await apiSaveFilter({
            startTime: state.timeRange.start,
            endTime: state.timeRange.end,
            minBreak: state.breakHours.min * 60,  // hours → minutes
            maxBreak: state.breakHours.max * 60,
            days,
            courseInstructors,
        });

        console.log('Filter saved, full response:', filter);
        console.log('Filter ID to use for generate:', filter.FId);
        state.currentFilterId = filter.FId;

        showLoading('Generating schedules…');

        // Step 2 — run the algorithm
        const schedules = await apiGenerateSchedules(filter.FId);

        if (schedules.length === 0) {
            state.generatedSchedules = [];
            state.noSchedulesFound = true;
        } else {
            state.generatedSchedules = schedules;
            state.currentScheduleIndex = 0;
            state.noSchedulesFound = false;

            // Restore starred schedules from server
            try {
                const favs = await apiGetFavourites(filter.FId);
                state.favoriteIds = (favs || []).map(f => String(f.SchedId));
            } catch (_) { /* non-critical */ }
        }
    } catch (e) {
        alert('Error generating schedules: ' + e.message);
    }

    hideLoading();
    render();
}

// ============================================================
//  TOGGLE FAVOURITE  (async — calls the real API)
// ============================================================
async function toggleFavorite(scheduleId) {
    if (!state.currentFilterId) {
        // Client-side only (no filter saved yet — shouldn't happen normally)
        if (state.favoriteIds.includes(scheduleId)) {
            state.favoriteIds = state.favoriteIds.filter(id => id !== scheduleId);
        } else {
            state.favoriteIds = [...state.favoriteIds, scheduleId];
        }
        render();
        return;
    }

    try {
        const result = await apiToggleFavourite(state.currentFilterId, scheduleId);
        if (result && result.Added) {
            state.favoriteIds = [...state.favoriteIds, scheduleId];
        } else {
            state.favoriteIds = state.favoriteIds.filter(id => id !== scheduleId);
        }
    } catch (e) {
        alert('Could not update favourites: ' + e.message);
    }

    render();
}

// ============================================================
//  START
// ============================================================
init();