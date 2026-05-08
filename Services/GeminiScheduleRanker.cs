using Google.GenAI;
using System.Text.Json;

namespace SmartSchedulingSystem.Services
{
    public class GeminiScheduleRanker
    {
        private readonly IConfiguration _config;

        public GeminiScheduleRanker(IConfiguration config)
        {
            _config = config;
        }

        public async Task<List<int>> RankSchedulesAsync(object requestData)
        {
            var apiKey = _config["Gemini:ApiKey"];
            var model = _config["Gemini:Model"] ?? "gemini-2.5-flash";

            if (string.IsNullOrEmpty(apiKey))
                throw new Exception("Gemini API key is missing.");

            var client = new Client(apiKey: apiKey);

            var prompt = """
            You are an academic schedule ranking assistant.

            Rank the schedules from most fit to least fit for this student.

                        Prerequisite awareness rules (use as soft guidance for ranking, not strict filtering):

            Recognize the following prerequisite relationships:

            - Discrete Mathematics 1 → Discrete Mathematics 2
            - Calculus 1 → Calculus 2
            - Physics 1 → Physics 2
            - Data Structures → Algorithm Design and Analysis
            - Data Structures + Assembly → Operating Systems
            - Data Communication and Computer Networks → Information System Security
            - Data Structures → Data Communication and Computer Networks
            - Intro to Computer Science → Structured Programming
            - Structured Programming → Object Oriented Programming (OOP)
            - OOP + Discrete Mathematics 1 → Data Structures
            - Digital Logic Design → Assembly and Computer Organization
            - Statistical Methods + Calculus 2 → Principles of Probability
            - Discrete Mathematics 2 + OOP → Theory of Computation
            - Data Structures → Database Systems
            - OOP → Webpage Design and Internet Programming
            - OOP → Visual Programming
            - Database Systems → Software Engineering
            - Data Structures → Artificial Intelligence
            - Calculus 2 → Numerical Analysis
            - Assembly → Computer Architecture

            How to use them:
            - If a student performed poorly (low COURSE_GPA or failed) in a prerequisite course, treat the dependent course as risky.
            - Do NOT remove or invalidate schedules.
            - Instead, rank such schedules lower unless the course is important for graduation progress.
            - Balance risk and necessity when ranking schedules.

            Your response must start with { and end with }.
            Do not write explanations.
            Do not write markdown.
            Do not write "The".
            Use the following factors:
            1. Student overall GPA
            2. Completed courses and COURSE_GPA
            3. PASS_FLAG:
               - 1 means passed
               - 2 means failed
            4. Total credit hours
            5. Workload balance
            6. Graduation progress

            Important academic logic:
            - If the student did not perform well (low COURSE_GPA or failed) in previous courses that are related to courses in a schedule, treat those future courses as risky.

            Ranking rules:
            - If student GPA is low, prefer safer and more balanced schedules.
            - If student GPA is high, prefer schedules that speed up graduation.
            - Avoid schedules that overload the student with difficult courses at once.
            - Do not create new schedules.
            - Do not remove schedules.
            - Return ONLY valid JSON.
            - Return all schedule IDs exactly once.
            - Return schedule IDs sorted from best to worst.

            Return valid JSON ONLY.

            Required JSON format:
            {
              "sortedScheduleIds": [1, 2, 3],
              "explanations": [
                {
                  "scheduleId": 1,
                  "reason": "why this schedule is ranked in this position"
                },
                {
                  "scheduleId": 2,
                  "reason": "..."
                }
              ]
            }

            Rules:
            - Every scheduleId must appear exactly once in sortedScheduleIds.
            - explanations must include ALL scheduleIds.
            - The order of explanations must match sortedScheduleIds.
            - Keep explanations short but clear (1–3 sentences).
            - Do not include markdown.
            - Do not include text outside JSON.

            Input data:
            """ + JsonSerializer.Serialize(requestData);


            Console.WriteLine("==== DATA SENT TO GEMINI ====");
            Console.WriteLine(JsonSerializer.Serialize(requestData, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            var response = await client.Models.GenerateContentAsync(
                model: model,
                contents: prompt
            );

            var text = response.Candidates[0].Content.Parts[0].Text;

            text = text.Replace("```json", "")
                       .Replace("```", "")
                       .Trim();

            Console.WriteLine("==== GEMINI RAW RESPONSE ====");
            Console.WriteLine(text);

            var jsonStart = text.IndexOf('{');
            var jsonEnd = text.LastIndexOf('}');

            if (jsonStart == -1 || jsonEnd == -1)
            {
                throw new Exception("Gemini did not return JSON. Response: " + text);
            }

            var json = text.Substring(jsonStart, jsonEnd - jsonStart + 1);

            var result = JsonSerializer.Deserialize<GeminiRankResponse>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            return result?.SortedScheduleIds ?? new List<int>();
        }
    }

    public class GeminiRankResponse
    {
        public List<int> SortedScheduleIds { get; set; } = new();
    }
}