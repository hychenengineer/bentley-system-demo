using Microsoft.AspNetCore.Mvc;
using BentleyFormsService.Models.Dtos;
using BentleyFormsService.Repositories;
using BentleyFormsService.Services;

namespace BentleyFormsService.Controllers;

[ApiController]
[Route("forms/formDefinitions")]
public class FormDefinitionsController : ControllerBase
{
    private readonly IFormDefinitionRepository _definitionRepository;

    public FormDefinitionsController(IFormDefinitionRepository definitionRepository)
    {
        _definitionRepository = definitionRepository;
    }

    /// <summary>
    /// Retrieves all form definitions available for an iTwin project.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<FormDefinitionResponseDto>>> GetDefinitions([FromQuery] string? iTwinId)
    {
        var entities = await _definitionRepository.ListByITwinAsync(iTwinId);
        var dtos = entities.Select(e => new FormDefinitionResponseDto
        {
            Id = e.Id,
            ITwinId = e.ITwinId,
            DisplayName = e.DisplayName,
            Description = e.Description,
            WorkflowType = e.WorkflowType,
            Fields = e.Fields.Select(f => new FormFieldDto
            {
                Name = f.Name,
                DisplayName = f.DisplayName,
                DataType = f.DataType,
                IsRequired = f.IsRequired,
                Options = !string.IsNullOrWhiteSpace(f.OptionsJson) 
                    ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(f.OptionsJson) 
                    : null
            }).ToList()
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Retrieves details and schema fields for a specific form definition.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<FormDefinitionResponseDto>> GetDefinitionById(string id)
    {
        var e = await _definitionRepository.GetByIdAsync(id);
        if (e == null) return NotFound(new { error = $"Form definition '{id}' not found." });

        return Ok(new FormDefinitionResponseDto
        {
            Id = e.Id,
            ITwinId = e.ITwinId,
            DisplayName = e.DisplayName,
            Description = e.Description,
            WorkflowType = e.WorkflowType,
            Fields = e.Fields.Select(f => new FormFieldDto
            {
                Name = f.Name,
                DisplayName = f.DisplayName,
                DataType = f.DataType,
                IsRequired = f.IsRequired,
                Options = !string.IsNullOrWhiteSpace(f.OptionsJson) 
                    ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(f.OptionsJson) 
                    : null
            }).ToList()
        });
    }
}

[ApiController]
[Route("forms/workflows")]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowService _workflowService;

    public WorkflowsController(IWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    /// <summary>
    /// Retrieves the workflow state machine definition, allowed transitions, and editable properties.
    /// </summary>
    [HttpGet("{formType}")]
    public async Task<ActionResult<WorkflowResponseDto>> GetWorkflow(string formType, [FromQuery] string? iTwinId)
    {
        var dto = await _workflowService.GetWorkflowDetailsAsync(formType);
        if (dto == null) return NotFound(new { error = $"Workflow '{formType}' not found." });
        return Ok(dto);
    }
}
