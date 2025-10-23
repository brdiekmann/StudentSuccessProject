namespace GeminiDataParsingTestProject.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GeminiDataParsingTestProject.Data;
using GeminiDataParsingTestProject.Models;

public class SyllabusService
{
    private readonly string _geminiApiKey;
    private readonly CalendarDbContext _context;

    public SyllabusService(IConfiguration configuration, CalendarDbContext context)
    {
        _geminiApiKey = configuration["ApiKey"];
        _context = context;
    }

    public async Task<List<CalendarEvent>> ProcessSyllabusAsync(IFormFile file)
    {
        // Extract text from file
        string syllabusText = await ExtractTextFromFileAsync(file);

        // Parse with Gemini API
        var studyBlocks = await ParseSyllabusWithGeminiAsync(syllabusText);

        // Create calendar events
        var events = new List<CalendarEvent>();
        foreach (var block in studyBlocks)
        {
            var calendarEvent = new CalendarEvent
            {
                Title = block.Title,
                Start = block.StartDate,
                End = block.EndDate,
                AllDay = false,
                Color = block.EventType switch
                {
                    "exam" => "#dc3545",
                    "assignment" => "#ffc107",
                    "study" => "#28a745",
                    _ => "#007bff"
                }
            };

            _context.Events.Add(calendarEvent);
            events.Add(calendarEvent);
        }

        await _context.SaveChangesAsync();
        return events;
    }

    private async Task<string> ExtractTextFromFileAsync(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLower();

        using var stream = file.OpenReadStream();

        return extension switch
        {
            ".pdf" => await ExtractTextFromPdfAsync(stream),
            ".docx" => ExtractTextFromDocx(stream),
            ".txt" => await new StreamReader(stream).ReadToEndAsync(),
            _ => throw new NotSupportedException($"File type {extension} is not supported")
        };
    }

    private async Task<string> ExtractTextFromPdfAsync(Stream stream)
    {
        var text = new StringBuilder();

        // Copy stream to memory stream since iText7 requires seekable stream
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        memoryStream.Position = 0;

        using (var pdfReader = new PdfReader(memoryStream))
        using (var pdfDocument = new PdfDocument(pdfReader))
        {
            for (int i = 1; i <= pdfDocument.GetNumberOfPages(); i++)
            {
                var page = pdfDocument.GetPage(i);
                var strategy = new SimpleTextExtractionStrategy();
                string pageText = PdfTextExtractor.GetTextFromPage(page, strategy);
                text.AppendLine(pageText);
            }
        }

        return text.ToString();
    }

    private string ExtractTextFromDocx(Stream stream)
    {
        var text = new StringBuilder();

        using (var doc = WordprocessingDocument.Open(stream, false))
        {
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    text.AppendLine(paragraph.InnerText);
                }
            }
        }

        return text.ToString();
    }

    private async Task<List<StudyBlock>> ParseSyllabusWithGeminiAsync(string syllabusText)
    {
        using var httpClient = new HttpClient();

        var prompt = $@"Analyze this college syllabus and extract all important dates, assignments, exams, and projects. 
For each item, create study blocks leading up to deadlines.

Rules:
1. For exams: Create 3-5 study blocks in the week leading up to the exam (1-2 hours each)
2. For major assignments/projects: Create study blocks starting 1-2 weeks before due date
3. For regular assignments: Create 1-2 study blocks 2-3 days before due date
4. Each study block should be 1-2 hours long
5. Spread study blocks across different days (avoid cramming)
6. Consider the current date is {DateTime.Now:yyyy-MM-dd}

Return ONLY a JSON array with this exact structure:
[
  {{
    ""title"": ""Study for Midterm Exam"",
    ""startDate"": ""2025-11-15T14:00:00"",
    ""endDate"": ""2025-11-15T16:00:00"",
    ""eventType"": ""study"",
    ""description"": ""Review chapters 1-5""
  }}
]

Event types: ""exam"", ""assignment"", ""study"", ""project""

Syllabus:
{syllabusText}";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 8192
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_geminiApiKey}",
            content
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API error: {errorContent}");
        }

        var responseContent = await response.Content.ReadAsStringAsync();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent, options);

        // Extract JSON from response
        var generatedText = geminiResponse?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? "";

        // Remove markdown code blocks if present
        generatedText = generatedText.Trim();
        if (generatedText.StartsWith("```json"))
        {
            generatedText = generatedText.Substring(7);
        }
        if (generatedText.StartsWith("```"))
        {
            generatedText = generatedText.Substring(3);
        }
        if (generatedText.EndsWith("```"))
        {
            generatedText = generatedText.Substring(0, generatedText.Length - 3);
        }
        generatedText = generatedText.Trim();

        var studyBlocks = JsonSerializer.Deserialize<List<StudyBlock>>(generatedText, options);
        return studyBlocks ?? new List<StudyBlock>();
    }
}