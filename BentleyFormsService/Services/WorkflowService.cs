using System.Text.Json;
using BentleyFormsService.Models.Dtos;
using BentleyFormsService.Repositories;

namespace BentleyFormsService.Services;

public interface IWorkflowService
{
    Task<(bool IsValid, string? Error)> ValidateTransitionAsync(string workflowType, string fromStatus, string toStatus);
    Task<(bool IsValid, string? Error)> ValidateEditablePropertiesAsync(string workflowType, string currentStatus, IEnumerable<string> propertyNames);
    Task<WorkflowResponseDto?> GetWorkflowDetailsAsync(string workflowType);
}

public class WorkflowService : IWorkflowService
{
    private readonly IWorkflowRepository _workflowRepository;

    public WorkflowService(IWorkflowRepository workflowRepository)
    {
        _workflowRepository = workflowRepository;
    }

    public async Task<(bool IsValid, string? Error)> ValidateTransitionAsync(string workflowType, string fromStatus, string toStatus)
    {
        var workflow = await _workflowRepository.GetByTypeAsync(workflowType);
        if (workflow == null)
        {
            return (false, $"Workflow '{workflowType}' not found.");
        }

        var currentState = workflow.States.FirstOrDefault(s => s.StateName.Equals(fromStatus, StringComparison.OrdinalIgnoreCase));
        if (currentState == null)
        {
            return (false, $"Current state '{fromStatus}' is not recognized in workflow '{workflowType}'.");
        }

        var allowedTransitions = JsonSerializer.Deserialize<List<string>>(currentState.AllowedTransitionsJson) ?? new();
        bool isAllowed = allowedTransitions.Any(t => t.Equals(toStatus, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
        {
            var allowedStr = allowedTransitions.Count > 0 ? string.Join(", ", allowedTransitions) : "None (terminal state)";
            return (false, $"Illegal status transition: cannot move from '{fromStatus}' to '{toStatus}'. Allowed transitions from '{fromStatus}' are: [{allowedStr}].");
        }

        return (true, null);
    }

    public async Task<(bool IsValid, string? Error)> ValidateEditablePropertiesAsync(string workflowType, string currentStatus, IEnumerable<string> propertyNames)
    {
        var workflow = await _workflowRepository.GetByTypeAsync(workflowType);
        if (workflow == null)
        {
            return (false, $"Workflow '{workflowType}' not found.");
        }

        var currentState = workflow.States.FirstOrDefault(s => s.StateName.Equals(currentStatus, StringComparison.OrdinalIgnoreCase));
        if (currentState == null)
        {
            return (false, $"Current state '{currentStatus}' is not recognized in workflow '{workflowType}'.");
        }

        var editableProperties = JsonSerializer.Deserialize<List<string>>(currentState.EditablePropertiesJson) ?? new();
        var disallowed = propertyNames
            .Where(prop => !editableProperties.Any(e => e.Equals(prop, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (disallowed.Count > 0)
        {
            var editableStr = editableProperties.Count > 0 ? string.Join(", ", editableProperties) : "None (form is locked in this state)";
            return (false, $"Fields [{string.Join(", ", disallowed)}] cannot be modified while form is in state '{currentStatus}'. Permitted editable fields in this state are: [{editableStr}].");
        }

        return (true, null);
    }

    public async Task<WorkflowResponseDto?> GetWorkflowDetailsAsync(string workflowType)
    {
        var workflow = await _workflowRepository.GetByTypeAsync(workflowType);
        if (workflow == null) return null;

        var dto = new WorkflowResponseDto
        {
            WorkflowType = workflow.WorkflowType,
            ITwinId = workflow.ITwinId,
            DisplayName = workflow.DisplayName,
            InitialState = workflow.InitialState,
            States = new Dictionary<string, WorkflowStateDetailsDto>()
        };

        foreach (var state in workflow.States)
        {
            dto.States[state.StateName] = new WorkflowStateDetailsDto
            {
                AllowedTransitions = JsonSerializer.Deserialize<List<string>>(state.AllowedTransitionsJson) ?? new(),
                EditableProperties = JsonSerializer.Deserialize<List<string>>(state.EditablePropertiesJson) ?? new()
            };
        }

        return dto;
    }
}
