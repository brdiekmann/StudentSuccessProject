using GeminiDataParsingTestProject.Models;
using GeminiDataParsingTestProject.Services;
using Microsoft.AspNetCore.Mvc;

namespace GeminiDataParsingTestProject.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SyllabusController : ControllerBase
    {
        private readonly SyllabusService _syllabusService;

        public SyllabusController(SyllabusService syllabusService)
        {
            _syllabusService = syllabusService;
        }

        [HttpPost("upload")]
        public async Task<ActionResult<SyllabusUploadResponse>> UploadSyllabus([FromForm] IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded");
            }

            // Validate file type
            var allowedExtensions = new[] { ".pdf", ".docx", ".txt" };
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest("Only PDF, DOCX, and TXT files are supported");
            }

            // Validate file size (max 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                return BadRequest("File size must be less than 10MB");
            }

            try
            {
                var events = await _syllabusService.ProcessSyllabusAsync(file);

                return Ok(new SyllabusUploadResponse
                {
                    Success = true,
                    Message = $"Successfully created {events.Count} study blocks",
                    EventsCreated = events.Count,
                    Events = events
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new SyllabusUploadResponse
                {
                    Success = false,
                    Message = $"Error processing syllabus: {ex.Message}"
                });
            }
        }
    }
}
