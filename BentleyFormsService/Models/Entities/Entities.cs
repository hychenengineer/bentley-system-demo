namespace BentleyFormsService.Models.Entities;

public class FormDefinitionEntity
{
    public string Id { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<FormFieldEntity> Fields { get; set; } = new();
}

public class FormFieldEntity
{
    public int Id { get; set; }
    public string FormDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string DataType { get; set; } = "string"; // string, number, boolean, select, date
    public bool IsRequired { get; set; } = false;
    public string? OptionsJson { get; set; } // JSON array of options for select fields
}

public class WorkflowDefinitionEntity
{
    public string WorkflowType { get; set; } = string.Empty;
    public string ITwinId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string InitialState { get; set; } = "Draft";

    public List<WorkflowStateEntity> States { get; set; } = new();
}

public class WorkflowStateEntity
{
    public int Id { get; set; }
    public string WorkflowType { get; set; } = string.Empty;
    public string StateName { get; set; } = string.Empty;
    public string AllowedTransitionsJson { get; set; } = "[]"; // e.g. ["Submitted"]
    public string EditablePropertiesJson { get; set; } = "[]"; // e.g. ["Location", "CrackDepth"]
}

public class FormDataEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string ITwinId { get; set; } = string.Empty;
    public string FormDefinitionId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Status { get; set; } = "Draft";
    public string PropertiesJson { get; set; } = "{}"; // JSON dictionary of values
    public DateTime CreatedDateTime { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = "FieldEngineer";
    public DateTime LastModifiedDateTime { get; set; } = DateTime.UtcNow;
    public string LastModifiedBy { get; set; } = "FieldEngineer";
    
    // AI Metadata & Provenance
    public bool AiExtracted { get; set; } = false;
    public double? AiConfidence { get; set; }
    public string? AiExtractionSummary { get; set; }

    public List<FormAuditLogEntity> AuditLogs { get; set; } = new();
}

public class FormAuditLogEntity
{
    public int Id { get; set; }
    public string FormDataId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Created, StatusTransition, PropertyUpdate, AiTriage
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "System";
    public string? Notes { get; set; }
}
