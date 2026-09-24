using System.Text.Json;
using BentleyFormsService.Models.Entities;

namespace BentleyFormsService.Data;

public static class DbInitializer
{
    public const string DefaultITwinId = "proj-hudson-tunnel-001";

    public static async Task InitializeAsync(FormsDbContext context)
    {
        await context.Database.EnsureCreatedAsync();

        if (context.FormDefinitions.Any())
        {
            return; // Already seeded
        }

        // 1. Seed Workflows
        var structuralWorkflow = new WorkflowDefinitionEntity
        {
            WorkflowType = "StructuralReviewWorkflow",
            ITwinId = DefaultITwinId,
            DisplayName = "Structural Concrete Engineering Review",
            InitialState = "Draft",
            States = new List<WorkflowStateEntity>
            {
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "Draft",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Submitted" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "Location", "DefectType", "Severity", "DepthInches", "ExposedRebar", "WaterLeakage", "InspectorNotes" })
                },
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "Submitted",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "UnderStructuralReview", "Draft" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "TriageNotes", "AssignedLead" })
                },
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "UnderStructuralReview",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Approved", "Rejected" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "EngineeringRemediationPlan", "ReviewerSignOff" })
                },
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "Approved",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Closed" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "VerificationPhotos", "CompletionDate" })
                },
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "Closed",
                    AllowedTransitionsJson = JsonSerializer.Serialize(Array.Empty<string>()),
                    EditablePropertiesJson = JsonSerializer.Serialize(Array.Empty<string>())
                },
                new()
                {
                    WorkflowType = "StructuralReviewWorkflow",
                    StateName = "Rejected",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Draft" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "RejectionReason" })
                }
            }
        };

        var safetyWorkflow = new WorkflowDefinitionEntity
        {
            WorkflowType = "SafetyEscalationWorkflow",
            ITwinId = DefaultITwinId,
            DisplayName = "OSHA & Site Safety Escalation",
            InitialState = "Draft",
            States = new List<WorkflowStateEntity>
            {
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "Draft",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Submitted" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "Zone", "HazardCategory", "TrenchDepthFeet", "ShoringPresent", "ImmediateAction", "WitnessCount" })
                },
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "Submitted",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "SafetyHalt", "UnderSafetyReview" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "SafetyOfficerNotes" })
                },
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "SafetyHalt",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Resolved" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "CorrectiveActionPlan", "HaltLiftedBy" })
                },
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "UnderSafetyReview",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Resolved", "SafetyHalt" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "SafetyResolutionNotes" })
                },
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "Resolved",
                    AllowedTransitionsJson = JsonSerializer.Serialize(new[] { "Closed" }),
                    EditablePropertiesJson = JsonSerializer.Serialize(new[] { "CloseoutNotes" })
                },
                new()
                {
                    WorkflowType = "SafetyEscalationWorkflow",
                    StateName = "Closed",
                    AllowedTransitionsJson = JsonSerializer.Serialize(Array.Empty<string>()),
                    EditablePropertiesJson = JsonSerializer.Serialize(Array.Empty<string>())
                }
            }
        };

        await context.Workflows.AddRangeAsync(structuralWorkflow, safetyWorkflow);

        // 2. Seed Form Definitions
        var concreteDef = new FormDefinitionEntity
        {
            Id = "def-concrete-quality",
            ITwinId = DefaultITwinId,
            DisplayName = "Concrete Structural Defect Report",
            Description = "Inspection of concrete spalling, cracking, honeycombing, and structural voids.",
            WorkflowType = "StructuralReviewWorkflow",
            Fields = new List<FormFieldEntity>
            {
                new() { FormDefinitionId = "def-concrete-quality", Name = "Location", DisplayName = "Structural Location / Pier", DataType = "string", IsRequired = true },
                new() { FormDefinitionId = "def-concrete-quality", Name = "DefectType", DisplayName = "Defect Type", DataType = "select", IsRequired = true, OptionsJson = JsonSerializer.Serialize(new[] { "Spalling", "Crack", "Honeycombing", "Void", "ColdJoint" }) },
                new() { FormDefinitionId = "def-concrete-quality", Name = "Severity", DisplayName = "Severity Level", DataType = "select", IsRequired = true, OptionsJson = JsonSerializer.Serialize(new[] { "Low", "Medium", "High", "Critical" }) },
                new() { FormDefinitionId = "def-concrete-quality", Name = "DepthInches", DisplayName = "Defect Depth (inches)", DataType = "number", IsRequired = true },
                new() { FormDefinitionId = "def-concrete-quality", Name = "ExposedRebar", DisplayName = "Exposed Reinforcing Steel (Rebar)", DataType = "boolean", IsRequired = true },
                new() { FormDefinitionId = "def-concrete-quality", Name = "WaterLeakage", DisplayName = "Active Water Infiltration", DataType = "boolean", IsRequired = true },
                new() { FormDefinitionId = "def-concrete-quality", Name = "InspectorNotes", DisplayName = "Inspector Field Observation Notes", DataType = "string", IsRequired = false }
            }
        };

        var safetyDef = new FormDefinitionEntity
        {
            Id = "def-site-safety-hazard",
            ITwinId = DefaultITwinId,
            DisplayName = "Site Safety Hazard Incident",
            Description = "Safety observations, excavation hazards, PPE violations, and stop-work events.",
            WorkflowType = "SafetyEscalationWorkflow",
            Fields = new List<FormFieldEntity>
            {
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "Zone", DisplayName = "Construction Site Zone", DataType = "string", IsRequired = true },
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "HazardCategory", DisplayName = "Hazard Category", DataType = "select", IsRequired = true, OptionsJson = JsonSerializer.Serialize(new[] { "Excavation", "FallHazard", "Electrical", "Scaffolding", "ChemicalRunoff" }) },
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "TrenchDepthFeet", DisplayName = "Excavation / Trench Depth (feet)", DataType = "number", IsRequired = false },
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "ShoringPresent", DisplayName = "Protective Shoring Installed", DataType = "boolean", IsRequired = false },
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "ImmediateAction", DisplayName = "Immediate Mitigation Action Taken", DataType = "string", IsRequired = true },
                new() { FormDefinitionId = "def-site-safety-hazard", Name = "WitnessCount", DisplayName = "Number of Workers in Area", DataType = "number", IsRequired = false }
            }
        };

        await context.FormDefinitions.AddRangeAsync(concreteDef, safetyDef);

        // 3. Seed 1 Sample Initial Form Instance
        var sampleForm = new FormDataEntity
        {
            Id = "form-hudson-pier-01",
            ITwinId = DefaultITwinId,
            FormDefinitionId = "def-concrete-quality",
            Subject = "Pier 4 East Footing Defect Inspection",
            Status = "Draft",
            PropertiesJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["Location"] = "Pier 4 East Footing, Station 12+40",
                ["DefectType"] = "Spalling",
                ["Severity"] = "Critical",
                ["DepthInches"] = 5.0,
                ["ExposedRebar"] = true,
                ["WaterLeakage"] = true,
                ["InspectorNotes"] = "Found severe spalling on Pier 4 east face, rebar exposed and rusting with active moisture. Halted pour in Bay 2."
            }),
            CreatedBy = "Inspector John Miller",
            LastModifiedBy = "Inspector John Miller",
            AiExtracted = true,
            AiConfidence = 0.96,
            AiExtractionSummary = "Extracted from field dictation note via AI Agent. 6 fields mapped, 0 schema violations.",
            AuditLogs = new List<FormAuditLogEntity>
            {
                new()
                {
                    Action = "Created",
                    FromStatus = null,
                    ToStatus = "Draft",
                    Actor = "Inspector John Miller",
                    Notes = "Form initialized via AI field voice note transcription."
                }
            }
        };

        await context.Forms.AddAsync(sampleForm);
        await context.SaveChangesAsync();
    }
}
