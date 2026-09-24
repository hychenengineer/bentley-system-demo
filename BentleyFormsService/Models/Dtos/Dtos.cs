using System.Text.Json.Serialization;

namespace BentleyFormsService.Models.Dtos;

public class CreateFormDto
{
    [JsonPropertyName("iTwinId")]
    public string ITwinId { get; set; } = string.Empty;

    [JsonPropertyName("formId")]
    public string FormId { get; set; } = string.Empty; // In Bentley API, formId in body refers to definition ID

    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?> Properties { get; set; } = new();
}

public class UpdateFormDto
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, object?>? Properties { get; set; }

    [JsonPropertyName("workflowNote")]
    public string? WorkflowNote { get; set; }
}

public class FormResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object?> Properties { get; set; } = new();
    public DateTime CreatedDateTime { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime LastModifiedDateTime { get; set; }
    public string LastModifiedBy { get; set; } = string.Empty;
    public bool AiExtracted { get; set; }
    public double? AiConfidence { get; set; }
    public string? AiExtractionSummary { get; set; }
    public List<AuditLogDto> AuditLogs { get; set; } = new();
}

public class AuditLogDto
{
    public string Action { get; set; } = string.Empty;
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTime Timestamp { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class FormDefinitionResponseDto
{
    public string Id { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
    public List<FormFieldDto> Fields { get; set; } = new();
}

public class FormFieldDto
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public bool IsRequired { get; set; }
    public List<string>? Options { get; set; }
}

public class WorkflowResponseDto
{
    public string WorkflowType { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InitialState { get; set; } = "Draft";
    public Dictionary<string, WorkflowStateDetailsDto> States { get; set; } = new();
}

public class WorkflowStateDetailsDto
{
    public List<string> AllowedTransitions { get; set; } = new();
    public List<string> EditableProperties { get; set; } = new();
}

public class AiExtractAndCreateRequestDto
{
    [JsonPropertyName("iTwinId")]
    public string ITwinId { get; set; } = string.Empty;

    [JsonPropertyName("rawInput")]
    public string RawInput { get; set; } = string.Empty;

    [JsonPropertyName("suggestedFormDefinitionId")]
    public string? SuggestedFormDefinitionId { get; set; }

    [JsonPropertyName("enableFallback")]
    public bool? EnableFallback { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}

public class AiExtractAndCreateResponseDto
{
    public bool Success { get; set; } = true;
    public string? RejectionReason { get; set; }
    public string FormId { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string MatchedDefinitionId { get; set; } = string.Empty;
    public string MatchedDefinitionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object?> Properties { get; set; } = new();
    public double Confidence { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string FormUrl { get; set; } = string.Empty;

    [JsonPropertyName("modelUsed")]
    public string? ModelUsed { get; set; }
}

public class AiTriageRequestDto
{
    [JsonPropertyName("applyTransition")]
    public bool ApplyTransition { get; set; } = false;

    [JsonPropertyName("triageRole")]
    public string? TriageRole { get; set; } = "AiSafetyOfficer";

    [JsonPropertyName("enableFallback")]
    public bool? EnableFallback { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }
}

public class AiTriageResponseDto
{
    public string FormId { get; set; } = string.Empty;
    public string PreviousStatus { get; set; } = string.Empty;
    public string RecommendedStatus { get; set; } = string.Empty;
    public bool TransitionApplied { get; set; }
    public string SeverityScore { get; set; } = "MEDIUM"; // LOW, MEDIUM, HIGH, CRITICAL
    public string RiskCategory { get; set; } = string.Empty;
    public string AiRecommendation { get; set; } = string.Empty;
    public string SuggestedAssignee { get; set; } = string.Empty;
}
