using System.Text.Json;
using BentleyFormsService.Models.Dtos;
using BentleyFormsService.Models.Entities;
using BentleyFormsService.Repositories;

namespace BentleyFormsService.Services;

public interface IFormService
{
    Task<FormResponseDto> CreateFormAsync(CreateFormDto dto, string actor = "FieldEngineer");
    Task<FormResponseDto?> GetFormByIdAsync(string id);
    Task<List<FormResponseDto>> ListFormsAsync(string? iTwinId, string? formDefinitionId, int top = 50, int skip = 0);
    Task<(bool Success, string? Error, FormResponseDto? Form)> UpdateFormAsync(string id, UpdateFormDto dto, string actor = "FieldEngineer");
    Task<bool> DeleteFormAsync(string id);
}

public class FormService : IFormService
{
    private readonly IFormRepository _formRepository;
    private readonly IFormDefinitionRepository _definitionRepository;
    private readonly IWorkflowService _workflowService;

    public FormService(
        IFormRepository formRepository,
        IFormDefinitionRepository definitionRepository,
        IWorkflowService workflowService)
    {
        _formRepository = formRepository;
        _definitionRepository = definitionRepository;
        _workflowService = workflowService;
    }

    public async Task<FormResponseDto> CreateFormAsync(CreateFormDto dto, string actor = "FieldEngineer")
    {
        if (string.IsNullOrWhiteSpace(dto.ITwinId))
            throw new ArgumentException("iTwinId is required.");

        if (string.IsNullOrWhiteSpace(dto.FormId))
            throw new ArgumentException("formId (Form Definition ID) is required.");

        var definition = await _definitionRepository.GetByIdAsync(dto.FormId);
        if (definition == null)
            throw new KeyNotFoundException($"Form definition '{dto.FormId}' does not exist.");

        // Check required fields
        var missingRequired = definition.Fields
            .Where(f => f.IsRequired && (!dto.Properties.ContainsKey(f.Name) || dto.Properties[f.Name] == null || string.IsNullOrWhiteSpace(dto.Properties[f.Name]?.ToString())))
            .Select(f => f.DisplayName)
            .ToList();

        if (missingRequired.Count > 0)
        {
            throw new InvalidOperationException($"Validation failed: Missing required fields [{string.Join(", ", missingRequired)}].");
        }

        var status = !string.IsNullOrWhiteSpace(dto.Status) ? dto.Status : "Draft";
        var formEntity = new FormDataEntity
        {
            Id = "form-" + Guid.NewGuid().ToString("N")[..10],
            ITwinId = dto.ITwinId,
            FormDefinitionId = dto.FormId,
            Subject = !string.IsNullOrWhiteSpace(dto.Subject) ? dto.Subject : $"{definition.DisplayName} - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Status = status,
            PropertiesJson = JsonSerializer.Serialize(dto.Properties),
            CreatedBy = actor,
            LastModifiedBy = actor,
            CreatedDateTime = DateTime.UtcNow,
            LastModifiedDateTime = DateTime.UtcNow,
            AuditLogs = new List<FormAuditLogEntity>
            {
                new()
                {
                    Action = "Created",
                    FromStatus = null,
                    ToStatus = status,
                    Actor = actor,
                    Timestamp = DateTime.UtcNow,
                    Notes = "Form created."
                }
            }
        };

        var created = await _formRepository.CreateAsync(formEntity);
        return MapToDto(created);
    }

    public async Task<FormResponseDto?> GetFormByIdAsync(string id)
    {
        var entity = await _formRepository.GetByIdAsync(id);
        return entity == null ? null : MapToDto(entity);
    }

    public async Task<List<FormResponseDto>> ListFormsAsync(string? iTwinId, string? formDefinitionId, int top = 50, int skip = 0)
    {
        var entities = await _formRepository.ListAsync(iTwinId, formDefinitionId, top, skip);
        return entities.Select(MapToDto).ToList();
    }

    public async Task<(bool Success, string? Error, FormResponseDto? Form)> UpdateFormAsync(string id, UpdateFormDto dto, string actor = "FieldEngineer")
    {
        var form = await _formRepository.GetByIdAsync(id);
        if (form == null) return (false, "Form not found.", null);

        var definition = await _definitionRepository.GetByIdAsync(form.FormDefinitionId);
        if (definition == null) return (false, "Associated FormDefinition not found.", null);

        // 1. Status Transition Validation
        if (!string.IsNullOrWhiteSpace(dto.Status) && !dto.Status.Equals(form.Status, StringComparison.OrdinalIgnoreCase))
        {
            var (validTransition, transError) = await _workflowService.ValidateTransitionAsync(definition.WorkflowType, form.Status, dto.Status);
            if (!validTransition)
            {
                return (false, transError, null);
            }

            var oldStatus = form.Status;
            form.Status = dto.Status;
            form.AuditLogs.Add(new FormAuditLogEntity
            {
                Action = "StatusTransition",
                FromStatus = oldStatus,
                ToStatus = dto.Status,
                Actor = actor,
                Timestamp = DateTime.UtcNow,
                Notes = dto.WorkflowNote ?? $"Status transitioned from {oldStatus} to {dto.Status}"
            });
        }

        // 2. Editable Properties Validation
        if (dto.Properties != null && dto.Properties.Count > 0)
        {
            var (validProps, propsError) = await _workflowService.ValidateEditablePropertiesAsync(definition.WorkflowType, form.Status, dto.Properties.Keys);
            if (!validProps)
            {
                return (false, propsError, null);
            }

            var currentProperties = JsonSerializer.Deserialize<Dictionary<string, object?>>(form.PropertiesJson) ?? new();
            foreach (var kvp in dto.Properties)
            {
                currentProperties[kvp.Key] = kvp.Value;
            }
            form.PropertiesJson = JsonSerializer.Serialize(currentProperties);

            form.AuditLogs.Add(new FormAuditLogEntity
            {
                Action = "PropertyUpdate",
                FromStatus = form.Status,
                ToStatus = form.Status,
                Actor = actor,
                Timestamp = DateTime.UtcNow,
                Notes = $"Updated fields: [{string.Join(", ", dto.Properties.Keys)}]"
            });
        }

        if (!string.IsNullOrWhiteSpace(dto.Subject))
        {
            form.Subject = dto.Subject;
        }

        form.LastModifiedBy = actor;
        await _formRepository.UpdateAsync(form);

        return (true, null, MapToDto(form));
    }

    public async Task<bool> DeleteFormAsync(string id)
    {
        return await _formRepository.DeleteAsync(id);
    }

    private static FormResponseDto MapToDto(FormDataEntity entity)
    {
        Dictionary<string, object?> properties;
        try
        {
            properties = JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.PropertiesJson) ?? new();
        }
        catch
        {
            properties = new();
        }

        return new FormResponseDto
        {
            Id = entity.Id,
            ITwinId = entity.ITwinId,
            FormId = entity.FormDefinitionId,
            Subject = entity.Subject,
            Status = entity.Status,
            Properties = properties,
            CreatedDateTime = entity.CreatedDateTime,
            CreatedBy = entity.CreatedBy,
            LastModifiedDateTime = entity.LastModifiedDateTime,
            LastModifiedBy = entity.LastModifiedBy,
            AiExtracted = entity.AiExtracted,
            AiConfidence = entity.AiConfidence,
            AiExtractionSummary = entity.AiExtractionSummary,
            AuditLogs = entity.AuditLogs.Select(log => new AuditLogDto
            {
                Action = log.Action,
                FromStatus = log.FromStatus,
                ToStatus = log.ToStatus,
                Timestamp = log.Timestamp,
                Actor = log.Actor,
                Notes = log.Notes
            }).OrderByDescending(l => l.Timestamp).ToList()
        };
    }
}
