using Microsoft.AspNetCore.Mvc;
using BentleyFormsService.Models.Dtos;
using BentleyFormsService.Services;

namespace BentleyFormsService.Controllers;

[ApiController]
[Route("forms")]
public class FormsController : ControllerBase
{
    private readonly IFormService _formService;

    public FormsController(IFormService formService)
    {
        _formService = formService;
    }

    /// <summary>
    /// Creates a new form instance (Standard Manual Entry or Integration).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FormResponseDto>> CreateForm([FromBody] CreateFormDto dto)
    {
        try
        {
            var created = await _formService.CreateFormAsync(dto);
            return CreatedAtAction(nameof(GetFormById), new { id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Retrieves a list of form instances for an iTwin project with optional filtering and pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FormResponseDto>>> GetForms(
        [FromQuery] string? iTwinId,
        [FromQuery] string? formDefinitionId,
        [FromQuery(Name = "$top")] int top = 50,
        [FromQuery(Name = "$skip")] int skip = 0)
    {
        var forms = await _formService.ListFormsAsync(iTwinId, formDefinitionId, top, skip);
        return Ok(forms);
    }

    /// <summary>
    /// Retrieves details, properties, and audit history for a specific form instance.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<FormResponseDto>> GetFormById(string id)
    {
        var form = await _formService.GetFormByIdAsync(id);
        if (form == null) return NotFound(new { error = $"Form '{id}' not found." });
        return Ok(form);
    }

    /// <summary>
    /// Updates form fields or transitions workflow status (enforcing state machine & editableProperties).
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<ActionResult<FormResponseDto>> UpdateForm(string id, [FromBody] UpdateFormDto dto)
    {
        var (success, error, updated) = await _formService.UpdateFormAsync(id, dto);
        if (!success)
        {
            return BadRequest(new { error });
        }
        return Ok(updated);
    }

    /// <summary>
    /// Deletes a form instance.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteForm(string id)
    {
        var deleted = await _formService.DeleteFormAsync(id);
        if (!deleted) return NotFound(new { error = $"Form '{id}' not found." });
        return NoContent();
    }

    /// <summary>
    /// Deletes all form instances for an iTwin project (clean all tickets).
    /// </summary>
    [HttpDelete("clean-all")]
    public async Task<IActionResult> CleanAllForms([FromQuery] string? iTwinId)
    {
        var forms = await _formService.ListFormsAsync(iTwinId, null, 10000, 0);
        int count = 0;
        foreach (var form in forms)
        {
            await _formService.DeleteFormAsync(form.Id);
            count++;
        }
        return Ok(new { message = $"Cleaned {count} form(s)." });
    }
}

[ApiController]
[Route("forms")]
public class AiFormsController : ControllerBase
{
    private readonly IAiFormAgent _aiAgent;

    public AiFormsController(IAiFormAgent aiAgent)
    {
        _aiAgent = aiAgent;
    }

    /// <summary>
    /// AI Field Intake: Ingests unstructured notes/voice transcript, extracts fields, and auto-creates draft form.
    /// </summary>
    [HttpPost("ai/extract-and-create")]
    public async Task<ActionResult<AiExtractAndCreateResponseDto>> ExtractAndCreate([FromBody] AiExtractAndCreateRequestDto request)
    {
        try
        {
            var result = await _aiAgent.ExtractAndCreateAsync(request);
            if (!result.Success)
            {
                return Ok(result);
            }
            return Created(result.FormUrl, result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// AI Risk Triage: Evaluates form content, severity, and suggests/applies workflow state machine transition.
    /// </summary>
    [HttpPost("{id}/ai/triage")]
    public async Task<ActionResult<AiTriageResponseDto>> TriageForm(string id, [FromBody] AiTriageRequestDto request)
    {
        try
        {
            var result = await _aiAgent.TriageFormAsync(id, request);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
