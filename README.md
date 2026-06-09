# Smart Scheduling System

An intelligent academic scheduling platform that helps university students generate, filter, and rank course schedules based on their preferences, graduation requirements, and academic performance.

The system automatically generates conflict-free schedules, applies user-defined constraints, and leverages AI-powered ranking to recommend the most suitable schedules for each student.

---

## Features

### Student Authentication

* Secure student login system
* Session-based authentication
* Personalized schedule generation

### Course Management

* View remaining courses required for graduation
* Add and remove courses from the scheduling pool
* Select preferred instructors for each course

### Advanced Filtering

Students can customize schedule generation using:

* Available days
* Earliest class start time
* Latest class end time
* Minimum break duration
* Maximum break duration
* Desired credit hours

### Schedule Generation

The scheduling engine:

* Generates conflict-free schedules
* Respects instructor preferences
* Applies day and time constraints
* Validates break durations
* Supports online, blended, and face-to-face courses
* Handles compulsory and elective requirements

### AI-Powered Schedule Ranking

Schedules are evaluated using Google's Gemini API.

The AI considers:

* Student overall GPA
* Historical course performance
* Previously failed courses
* Course workload balance
* Graduation progress
* Core (backbone) program requirements
* Academic risk factors

Schedules are returned in ranked order from most suitable to least suitable.

### Favorites System

* Save preferred schedules
* Remove saved schedules
* View favorites across all filters

---

## System Architecture

The project follows a layered architecture:

```text
Controllers
│
├── Authentication
├── Course Management
├── Filter Management
├── Schedule Generation
└── Favorites

Services
│
├── Scheduler Service
├── Student Service
└── Gemini Schedule Ranker

Data Layer
│
└── Entity Framework Core + Oracle

Database
│
├── Students
├── Courses
├── Sections
├── Instructors
├── Filters
├── Generated Schedules
├── Favorites
└── Student Grades
```

---

## Technologies Used

### Backend

* ASP.NET Core Web API
* C#
* Entity Framework Core
* Oracle Database

### Frontend

* HTML
* CSS
* JavaScript

### Artificial Intelligence

* Google Gemini API
* Gemini 2.5 Flash

### Development Tools

* Visual Studio 2022
* SQL Developer
* Git & GitHub

---

## Database Design

### Main Entities

#### Student

Stores student information and overall GPA.

#### Course

Stores course metadata including:

* Course ID
* Name
* Credit Hours
* Requirement Type
* Delivery Mode

#### Section

Stores available course sections and instructors.

#### Filter

Stores user scheduling preferences.

#### Generated Schedule

Stores generated schedules and associated sections.

#### Favorite

Stores student favorite schedules.

#### Student Grade

Stores historical academic performance.

```text
GRADE_ID
ST_ID
COURSE_NAME
YEAR_ID
SEM_ID
CREDIT_HOURS
PASS_FLAG
COURSE_GPA
```

---

## Schedule Generation Algorithm

The scheduler:

1. Loads all selected courses.
2. Filters sections according to:

   * Instructor preferences
   * Allowed days
   * Time constraints
3. Generates valid course combinations.
4. Generates section combinations.
5. Eliminates conflicting schedules.
6. Validates break requirements.
7. Produces all valid schedules within the specified credit-hour limit.
8. Sends generated schedules to Gemini AI for ranking.

---

## AI Ranking Workflow

```text
Generated Schedules
        │
        ▼
Student GPA
Student Course History
Course Performance
        │
        ▼
Gemini API
        │
        ▼
Ranked Schedule IDs
        │
        ▼
Schedules Sorted
Best → Worst
```

The AI prioritizes:

* Graduation efficiency
* Core program requirements
* Academic success probability
* Balanced workload distribution

---

## Getting Started

### Prerequisites

* .NET 8 SDK
* Oracle Database
* Visual Studio 2022
* Google Gemini API Key

### Clone Repository

```bash
git clone https://github.com/Yazann570/Graduation-Project.git
cd Graduation-Project
```

### Configure Database

Update your connection string inside:

```json
appsettings.json
```

Example:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Your Oracle Connection String"
  }
}
```

### Configure Gemini API

Add your API key:

```json
{
  "Gemini": {
    "ApiKey": "YOUR_API_KEY",
    "Model": "gemini-2.5-flash"
  }
}
```

### Run the Project

```bash
dotnet restore
dotnet build
dotnet run
```

---

## Future Improvements

* Degree plan forecasting
* Graduation date prediction
* AI explanation of schedule rankings
* Mobile application support
* Calendar integration
* Real-time registration synchronization
* Machine learning based performance prediction

---

## Project Team

Developed as a Graduation Project for the Bachelor of Software Engineering program.

---

## License

This project is developed for students at Princess Sumaya University for Technology to enhance their academic experience.
