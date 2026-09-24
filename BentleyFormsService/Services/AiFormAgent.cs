using System.Text.Json;
using System.Text.RegularExpressions;
using BentleyFormsService.Models.Dtos;
using BentleyFormsService.Repositories;
using Microsoft.Extensions.Logging;

namespace BentleyFormsService.Services;

public interface IAiFormAgent
{
    Task<AiExtractAndCreateResponseDto> ExtractAndCreateAsync(AiExtractAndCreateRequestDto request);
    Task<AiTriageResponseDto> TriageFormAsync(string formId, AiTriageRequestDto request);
}

public class AiFormAgent : IAiFormAgent
{
    private readonly IFormService _formService;
    private readonly IFormDefinitionRepository _definitionRepository;
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiFormAgent> _logger;

    public AiFormAgent(
        IFormService formService,
        IFormDefinitionRepository definitionRepository,
        IWorkflowRepository workflowRepository,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<AiFormAgent> logger)
    {
        _formService = formService;
        _definitionRepository = definitionRepository;
        _workflowRepository = workflowRepository;
        _configuration = configuration;
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task<AiExtractAndCreateResponseDto> ExtractAndCreateAsync(AiExtractAndCreateRequestDto request)
    {
        var rawText = request.RawInput ?? string.Empty;
        var iTwinId = string.IsNullOrWhiteSpace(request.ITwinId) ? "proj-hudson-tunnel-001" : request.ITwinId;

        // Check fallback configuration: defaults to false if turned off in appsettings
        bool defaultFallbackConfig = _configuration.GetValue<bool>("AiAgent:EnableFallbackToDomainEngine", false);
        bool allowFallback = request.EnableFallback ?? defaultFallbackConfig;

        // Check if Groq, Gemini, or OpenAI key is configured
        var groqKey = _configuration["Groq:ApiKey"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
        var geminiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        var openAiKey = _configuration["OpenAI:ApiKey"];

        string requestedModel = request.ModelId?.Trim() ?? string.Empty;
        bool isGeminiModel = !string.IsNullOrWhiteSpace(requestedModel) && requestedModel.StartsWith("gemini-");
        bool isGroqModel = !string.IsNullOrWhiteSpace(requestedModel) && (
            requestedModel.StartsWith("openai/") || 
            requestedModel.StartsWith("qwen/") || 
            requestedModel.StartsWith("allam") || 
            requestedModel.StartsWith("meta-llama/")
        );

        if (isGroqModel && !string.IsNullOrWhiteSpace(request.ApiKey)) {
            groqKey = request.ApiKey;
        } else if (isGeminiModel && !string.IsNullOrWhiteSpace(request.ApiKey)) {
            geminiKey = request.ApiKey;
        } else if (!string.IsNullOrWhiteSpace(request.ApiKey)) {
            // Default fallback if we can't infer from model name
            groqKey = request.ApiKey; 
            geminiKey = request.ApiKey;
        }

        bool useGroq = !string.IsNullOrWhiteSpace(groqKey) && !groqKey.StartsWith("YOUR_");
        bool useGemini = !string.IsNullOrWhiteSpace(geminiKey) && !geminiKey.StartsWith("YOUR_");
        bool useOpenAi = !string.IsNullOrWhiteSpace(openAiKey) && !openAiKey.StartsWith("YOUR_");

        bool routeToGroq = useGroq && (isGroqModel || !isGeminiModel);
        string activeModel = !string.IsNullOrWhiteSpace(requestedModel)
            ? requestedModel
            : (routeToGroq ? (_configuration["Groq:ModelId"] ?? "openai/gpt-oss-120b") : (_configuration["Gemini:ModelId"] ?? "gemini-3.1-flash-lite"));

        // Fetch project definitions dynamically from database
        var availableDefinitions = await _definitionRepository.ListByITwinAsync(iTwinId);

        string matchedDefId = "def-concrete-quality";
        string matchedDefName = "Concrete Structural Defect Report";
        var properties = new Dictionary<string, object?>();
        double confidence = 0.94;
        string summary = "";
        string? rejectionReason = null;
        bool extractedViaLlm = false;
        string? lastLlmError = null;

        // 1. Try Groq API if routed to Groq
        if (routeToGroq && useGroq)
        {
            try
            {
                var groqResult = await CallGroqExtractionAsync(groqKey!, activeModel, rawText, availableDefinitions);
                if (groqResult != null)
                {
                    matchedDefId = groqResult.Value.MatchedDefId;
                    matchedDefName = groqResult.Value.MatchedDefName;
                    properties = groqResult.Value.Properties;
                    confidence = groqResult.Value.Confidence;
                    summary = groqResult.Value.Summary;
                    rejectionReason = groqResult.Value.RejectionReason;
                    extractedViaLlm = true;
                    _logger.LogInformation("Successfully processed intake using Groq ({Model}): Def={Def}, Conf={Conf:F2}", activeModel, matchedDefId, confidence);
                }
                else
                {
                    lastLlmError = "Groq model returned empty response.";
                }
            }
            catch (Exception ex)
            {
                lastLlmError = ex.Message;
                _logger.LogWarning(ex, "Groq extraction failed: {Message}. allowFallback={AllowFallback}", ex.Message, allowFallback);
            }
        }

        // 2. Try Google Gemini API if routed or fallback from Groq
        if (!extractedViaLlm && useGemini && !routeToGroq)
        {
            try
            {
                var geminiResult = await CallGeminiExtractionAsync(geminiKey!, activeModel, rawText, availableDefinitions);
                if (geminiResult != null)
                {
                    matchedDefId = geminiResult.Value.MatchedDefId;
                    matchedDefName = geminiResult.Value.MatchedDefName;
                    properties = geminiResult.Value.Properties;
                    confidence = geminiResult.Value.Confidence;
                    summary = geminiResult.Value.Summary;
                    rejectionReason = geminiResult.Value.RejectionReason;
                    extractedViaLlm = true;
                    _logger.LogInformation("Successfully processed intake using Google Gemini ({Model}): Def={Def}, Conf={Conf:F2}", activeModel, matchedDefId, confidence);
                }
                else
                {
                    lastLlmError = "Gemini model returned empty response.";
                }
            }
            catch (Exception ex)
            {
                lastLlmError = ex.Message;
                _logger.LogWarning(ex, "Gemini extraction failed: {Message}. allowFallback={AllowFallback}", ex.Message, allowFallback);
            }
        }

        // 3. Try OpenAI API if other LLMs not used/failed
        if (!extractedViaLlm && useOpenAi)
        {
            try
            {
                var openAiResult = await CallOpenAiExtractionAsync(openAiKey!, rawText);
                if (openAiResult != null)
                {
                    matchedDefId = openAiResult.Value.MatchedDefId;
                    matchedDefName = openAiResult.Value.MatchedDefName;
                    properties = openAiResult.Value.Properties;
                    confidence = openAiResult.Value.Confidence;
                    summary = openAiResult.Value.Summary;
                    extractedViaLlm = true;
                }
                else
                {
                    lastLlmError = "OpenAI model returned empty response.";
                }
            }
            catch (Exception ex)
            {
                lastLlmError = ex.Message;
                _logger.LogWarning(ex, "OpenAI extraction failed: {Message}. allowFallback={AllowFallback}", ex.Message, allowFallback);
            }
        }

        // 3. Fallback to Built-in Intelligent Domain Engine or Return Failure
        if (!extractedViaLlm)
        {
            if (!allowFallback)
            {
                var failureMsg = lastLlmError != null 
                    ? $"LLM extraction failed ({lastLlmError}). Fallback engine is disabled." 
                    : (!useGroq && !useGemini && !useOpenAi 
                        ? "No LLM API keys configured and fallback engine is disabled." 
                        : "LLM extraction did not succeed and fallback engine is disabled.");

                _logger.LogInformation("AI Form Intake rejected input: MatchedDefId=NONE, Reason={Reason}", failureMsg);

                return new AiExtractAndCreateResponseDto
                {
                    Success = false,
                    FormId = string.Empty,
                    ITwinId = iTwinId,
                    MatchedDefinitionId = "NONE",
                    MatchedDefinitionName = "LLM Extraction Failed",
                    Status = "Failed",
                    Properties = new Dictionary<string, object?>(),
                    Confidence = 0.0,
                    Summary = failureMsg,
                    RejectionReason = failureMsg,
                    FormUrl = string.Empty,
                    ModelUsed = activeModel
                };
            }

            ExtractViaDomainEngine(rawText, out matchedDefId, out matchedDefName, out properties, out confidence, out summary, out rejectionReason);
        }

        // 4. GUARDRAIL: Out-of-Domain or Low Confidence Rejection Gate
        if (string.Equals(matchedDefId, "NONE", StringComparison.OrdinalIgnoreCase) || confidence < 0.65)
        {
            _logger.LogInformation("AI Form Intake rejected input: MatchedDefId={DefId}, Confidence={Confidence:F2}, Reason={Reason}", matchedDefId, confidence, rejectionReason);

            return new AiExtractAndCreateResponseDto
            {
                Success = false,
                FormId = string.Empty,
                ITwinId = iTwinId,
                MatchedDefinitionId = "NONE",
                MatchedDefinitionName = "No Matching Form",
                Status = "Unmatched",
                Properties = new Dictionary<string, object?>(),
                Confidence = confidence,
                Summary = summary,
                RejectionReason = rejectionReason ?? "Input does not match any active civil engineering inspection template (Confidence too low).",
                FormUrl = string.Empty,
                ModelUsed = extractedViaLlm ? activeModel : "DomainRuleEngine"
            };
        }

        // Sanitize & map properties against the matched form definition schema
        properties = SanitizeAndMapProperties(properties, matchedDefId, availableDefinitions);

        // Delegate to Core Form Creation Engine
        var createDto = new CreateFormDto
        {
            ITwinId = iTwinId,
            FormId = matchedDefId,
            Subject = $"{matchedDefName} (AI-Assisted) - {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            Status = "Draft",
            Properties = properties
        };

        var createdForm = await _formService.CreateFormAsync(createDto, actor: extractedViaLlm ? (routeToGroq ? "AI_Agent_Groq" : "AI_Agent_Gemini") : "AI_Field_Agent_v1");

        return new AiExtractAndCreateResponseDto
        {
            Success = true,
            FormId = createdForm.Id,
            ITwinId = iTwinId,
            MatchedDefinitionId = matchedDefId,
            MatchedDefinitionName = matchedDefName,
            Status = createdForm.Status,
            Properties = properties,
            Confidence = confidence,
            Summary = summary,
            FormUrl = $"/forms/{createdForm.Id}",
            ModelUsed = extractedViaLlm ? activeModel : "DomainRuleEngine"
        };
    }

    public async Task<AiTriageResponseDto> TriageFormAsync(string formId, AiTriageRequestDto request)
    {
        var form = await _formService.GetFormByIdAsync(formId);
        if (form == null)
            throw new KeyNotFoundException($"Form with ID '{formId}' not found.");

        bool defaultFallbackConfig = _configuration.GetValue<bool>("AiAgent:EnableFallbackToDomainEngine", false);
        bool allowFallback = request.EnableFallback ?? defaultFallbackConfig;

        var groqKey = _configuration["Groq:ApiKey"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY");
        var geminiKey = _configuration["Gemini:ApiKey"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        string requestedModel = request.ModelId?.Trim() ?? string.Empty;
        bool isGeminiModel = !string.IsNullOrWhiteSpace(requestedModel) && requestedModel.StartsWith("gemini-");
        bool isGroqModel = !string.IsNullOrWhiteSpace(requestedModel) && (
            requestedModel.StartsWith("openai/") || 
            requestedModel.StartsWith("qwen/") || 
            requestedModel.StartsWith("allam") || 
            requestedModel.StartsWith("meta-llama/")
        );

        if (isGroqModel && !string.IsNullOrWhiteSpace(request.ApiKey)) {
            groqKey = request.ApiKey;
        } else if (isGeminiModel && !string.IsNullOrWhiteSpace(request.ApiKey)) {
            geminiKey = request.ApiKey;
        } else if (!string.IsNullOrWhiteSpace(request.ApiKey)) {
            groqKey = request.ApiKey; 
            geminiKey = request.ApiKey;
        }

        bool useGroq = !string.IsNullOrWhiteSpace(groqKey) && !groqKey.StartsWith("YOUR_");
        bool useGemini = !string.IsNullOrWhiteSpace(geminiKey) && !geminiKey.StartsWith("YOUR_");



        bool routeToGroq = useGroq && (isGroqModel || !isGeminiModel);
        string activeModel = !string.IsNullOrWhiteSpace(requestedModel)
            ? requestedModel
            : (routeToGroq ? (_configuration["Groq:ModelId"] ?? "openai/gpt-oss-120b") : (_configuration["Gemini:ModelId"] ?? "gemini-3.1-flash-lite"));

        string severityScore = "MEDIUM";
        string riskCategory = "General Observation";
        string aiRecommendation = "";
        string suggestedAssignee = "ProjectEngineer";
        string recommendedStatus = form.Status;
        bool triagedViaLlm = false;
        string? lastTriageError = null;

        // 1. Try Groq for Triage
        if (routeToGroq && useGroq)
        {
            try
            {
                var groqTriage = await CallGroqTriageAsync(groqKey!, activeModel, form);
                if (groqTriage != null)
                {
                    severityScore = groqTriage.SeverityScore;
                    riskCategory = groqTriage.RiskCategory;
                    aiRecommendation = groqTriage.AiRecommendation;
                    suggestedAssignee = groqTriage.SuggestedAssignee;
                    recommendedStatus = groqTriage.RecommendedStatus;
                    triagedViaLlm = true;
                    _logger.LogInformation("Successfully performed engineering triage using Groq ({Model})", activeModel);
                }
                else
                {
                    lastTriageError = "Groq triage returned empty response.";
                }
            }
            catch (Exception ex)
            {
                lastTriageError = ex.Message;
                _logger.LogWarning(ex, "Groq triage failed: {Message}. allowFallback={AllowFallback}", ex.Message, allowFallback);
            }
        }

        // 2. Try Google Gemini for Triage
        if (!triagedViaLlm && useGemini && !routeToGroq)
        {
            try
            {
                var geminiTriage = await CallGeminiTriageAsync(geminiKey!, activeModel, form);
                if (geminiTriage != null)
                {
                    severityScore = geminiTriage.SeverityScore;
                    riskCategory = geminiTriage.RiskCategory;
                    aiRecommendation = geminiTriage.AiRecommendation;
                    suggestedAssignee = geminiTriage.SuggestedAssignee;
                    recommendedStatus = geminiTriage.RecommendedStatus;
                    triagedViaLlm = true;
                    _logger.LogInformation("Successfully performed engineering triage using Google Gemini ({Model})", activeModel);
                }
                else
                {
                    lastTriageError = "Gemini triage returned empty response.";
                }
            }
            catch (Exception ex)
            {
                lastTriageError = ex.Message;
                _logger.LogWarning(ex, "Gemini triage failed: {Message}. allowFallback={AllowFallback}", ex.Message, allowFallback);
            }
        }

        // 3. Fallback to Built-in Domain Rule Engine
        if (!triagedViaLlm)
        {
            if (!allowFallback)
            {
                var errDetail = lastTriageError ?? (!useGroq && !useGemini ? "No LLM API keys configured" : "LLM triage failed");
                throw new InvalidOperationException($"LLM triage failed ({errDetail}) and fallback engine is disabled.");
            }

            TriageViaDomainEngine(form, out severityScore, out riskCategory, out aiRecommendation, out suggestedAssignee, out recommendedStatus);
        }

        bool transitionApplied = false;
        if (request.ApplyTransition && !string.Equals(form.Status, recommendedStatus, StringComparison.OrdinalIgnoreCase))
        {
            var updateResult = await _formService.UpdateFormAsync(formId, new UpdateFormDto
            {
                Status = recommendedStatus,
                WorkflowNote = $"[AI Automated Triage]: {aiRecommendation}"
            }, actor: request.TriageRole ?? (triagedViaLlm ? (routeToGroq ? "Groq_Safety_Agent" : "Gemini_Safety_Agent") : "AI_Safety_Agent"));

            transitionApplied = updateResult.Success;
        }

        return new AiTriageResponseDto
        {
            FormId = formId,
            PreviousStatus = form.Status,
            RecommendedStatus = recommendedStatus,
            TransitionApplied = transitionApplied,
            SeverityScore = severityScore,
            RiskCategory = riskCategory,
            AiRecommendation = aiRecommendation,
            SuggestedAssignee = suggestedAssignee
        };
    }

    private async Task<(string MatchedDefId, string MatchedDefName, Dictionary<string, object?> Properties, double Confidence, string Summary, string? RejectionReason)?> CallGeminiExtractionAsync(
        string apiKey, 
        string modelId, 
        string rawInput, 
        List<BentleyFormsService.Models.Entities.FormDefinitionEntity> availableDefinitions)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

        var defsBuilder = new System.Text.StringBuilder();
        if (availableDefinitions != null && availableDefinitions.Count > 0)
        {
            foreach (var d in availableDefinitions)
            {
                defsBuilder.AppendLine($"- Form ID: \"{d.Id}\" | Name: \"{d.DisplayName}\"");
                defsBuilder.AppendLine($"  Description: {d.Description}");
                defsBuilder.AppendLine("  Fields:");
                foreach (var f in d.Fields)
                {
                    var req = f.IsRequired ? "required" : "optional";
                    var opts = !string.IsNullOrWhiteSpace(f.OptionsJson) ? $", Options: {f.OptionsJson}" : "";
                    defsBuilder.AppendLine($"    * {f.Name} (type: {f.DataType}, {req}{opts})");
                }
                defsBuilder.AppendLine();
            }
        }
        else
        {
            defsBuilder.AppendLine("- Form ID: \"def-concrete-quality\" | Name: \"Concrete Structural Defect Report\"");
            defsBuilder.AppendLine("- Form ID: \"def-site-safety-hazard\" | Name: \"Site Safety Hazard Incident\"");
        }

        var prompt = $$"""
You are an expert civil engineering and site inspection AI agent for Bentley Cloud Platform (iTwin Forms).
Your role is to classify unstructured inspector notes/voice transcripts into the correct structured inspection form and extract field values.

Available Form Definitions for this Infrastructure Project:
{{defsBuilder}}

CLASSIFICATION & OUT-OF-DOMAIN GUARDRAIL RULES:
1. Determine which available form definition best matches the inspector's observation.
2. If the note matches an available definition with high certainty (>= 0.65):
   - Set "matchedDefId" to that definition's exact ID.
   - Set "matchedDefName" to the definition's exact Display Name.
   - Extract the field properties into the "properties" dictionary with appropriate data types (numbers as numbers, booleans as booleans).
3. CRITICAL OUT-OF-DOMAIN / UNRELATED INPUT RULE:
   If the user input is NOT related to civil engineering infrastructure, construction safety, or any of the available inspection forms (e.g. food/lunch orders, weather chit-chat, personal remarks, IT support requests, jokes, or vague gibberish), OR if you are less than 65% confident:
   - You MUST set "matchedDefId": "NONE"
   - You MUST set "matchedDefName": "No Matching Form"
   - You MUST set "confidence": a low score between 0.05 and 0.40
   - You MUST set "rejectionReason": A polite, concise explanation of why the input was rejected and which inspection templates are supported.
   - Set "properties": {}

Analyze this raw inspector note:
"{{rawInput}}"

Output valid JSON matching this schema:
{
  "matchedDefId": "def-concrete-quality" or "def-site-safety-hazard" or "NONE",
  "matchedDefName": "Full Definition Display Name or 'No Matching Form'",
  "confidence": 0.95,
  "summary": "One-sentence factual summary of findings or reason for non-match",
  "rejectionReason": "Only provide when matchedDefId is NONE (e.g. Note discusses food catering, which does not match active civil inspection forms)",
  "properties": {
    "FieldName": "extracted value"
  }
}
""";

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json"
            }
        };

        try
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var res = await _httpClient.PostAsync(endpoint, jsonContent, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Gemini API returned status {StatusCode}: {ErrorBody}", res.StatusCode, errorBody);
                throw new HttpRequestException($"Gemini API returned HTTP {(int)res.StatusCode} ({res.StatusCode})");
            }

            var responseJson = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0) return null;

            var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var innerDoc = JsonDocument.Parse(text);
            var root = innerDoc.RootElement;

            string defId = root.TryGetProperty("matchedDefId", out var idProp) ? (idProp.GetString() ?? "NONE") : "NONE";
            string defName = root.TryGetProperty("matchedDefName", out var nameProp) ? (nameProp.GetString() ?? "No Matching Form") : "No Matching Form";
            double conf = root.TryGetProperty("confidence", out var cProp) ? cProp.GetDouble() : 0.95;
            string sum = root.TryGetProperty("summary", out var sProp) ? (sProp.GetString() ?? "") : "";
            string? rej = root.TryGetProperty("rejectionReason", out var rProp) ? rProp.GetString() : null;

            var props = new Dictionary<string, object?>();
            if (root.TryGetProperty("properties", out var propsElement) && propsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propsElement.EnumerateObject())
                {
                    props[prop.Name] = ConvertJsonElement(prop.Value);
                }
            }

            return (defId, defName, props, conf, sum, rej);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Gemini extraction timed out after 5s.");
            throw new TimeoutException("Gemini API call timed out after 5 seconds.");
        }
    }

    private async Task<AiTriageResponseDto?> CallGeminiTriageAsync(string apiKey, string modelId, FormResponseDto form)
    {
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={apiKey}";

        var prompt = $$"""
You are a Senior Structural & Safety Engineering Lead triaging a field inspection for Bentley Cloud Platform (iTwin Forms).
Evaluate the form submission properties and determine the severity, risk category, actionable engineering recommendation, suggested assignee, and recommended next workflow status.

Workflow States Available:
- For def-concrete-quality: 'Draft', 'Submitted', 'UnderStructuralReview', 'Approved', 'Rejected', 'PendingLabTest'
- For def-site-safety-hazard: 'Draft', 'Submitted', 'UnderSafetyReview', 'SafetyHalt', 'Resolved'

Form Data Under Evaluation:
Form ID: {{form.Id}}
Form Definition: {{form.FormId}}
Current Status: {{form.Status}}
Submitted Properties:
{{JsonSerializer.Serialize(form.Properties, new JsonSerializerOptions { WriteIndented = true })}}

Output valid JSON matching this schema:
{
  "severityScore": "CRITICAL",
  "riskCategory": "OSHA 1926 Subpart P Violation",
  "aiRecommendation": "Detailed 2-3 sentence technical engineering analysis citing applicable standards (e.g. OSHA, ACI 318, IBC) and concrete repair/mitigation steps.",
  "suggestedAssignee": "Lead Structural Engineer (P.E.)",
  "recommendedStatus": "UnderStructuralReview"
}
""";

        var requestBody = new
        {
            contents = new object[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                responseMimeType = "application/json"
            }
        };

        try
        {
            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var res = await _httpClient.PostAsync(endpoint, jsonContent, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Gemini Triage API returned status {StatusCode}: {ErrorBody}", res.StatusCode, errorBody);
                throw new HttpRequestException($"Gemini Triage API returned HTTP {(int)res.StatusCode} ({res.StatusCode})");
            }

            var responseJson = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(responseJson);
            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0) return null;

            var text = candidates[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var innerDoc = JsonDocument.Parse(text);
            var root = innerDoc.RootElement;

            return new AiTriageResponseDto
            {
                FormId = form.Id,
                PreviousStatus = form.Status,
                SeverityScore = root.TryGetProperty("severityScore", out var sev) ? (sev.GetString() ?? "HIGH") : "HIGH",
                RiskCategory = root.TryGetProperty("riskCategory", out var cat) ? (cat.GetString() ?? "Engineering Review") : "Engineering Review",
                AiRecommendation = root.TryGetProperty("aiRecommendation", out var rec) ? (rec.GetString() ?? "") : "",
                SuggestedAssignee = root.TryGetProperty("suggestedAssignee", out var ass) ? (ass.GetString() ?? "Project Engineer") : "Project Engineer",
                RecommendedStatus = root.TryGetProperty("recommendedStatus", out var stat) ? (stat.GetString() ?? form.Status) : form.Status
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Gemini triage timed out after 5s.");
            throw new TimeoutException("Gemini Triage API call timed out after 5 seconds.");
        }
    }

    private void TriageViaDomainEngine(FormResponseDto form, out string severityScore, out string riskCategory, out string aiRecommendation, out string suggestedAssignee, out string recommendedStatus)
    {
        severityScore = "MEDIUM";
        riskCategory = "General Observation";
        aiRecommendation = "";
        suggestedAssignee = "ProjectEngineer";
        recommendedStatus = form.Status;

        // Structural Concrete Inspection Logic
        if (form.FormId == "def-concrete-quality")
        {
            bool hasRebar = form.Properties.TryGetValue("ExposedRebar", out var reb) && (reb?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false);
            bool hasWater = form.Properties.TryGetValue("WaterLeakage", out var wat) && (wat?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false);
            double depth = 0;
            if (form.Properties.TryGetValue("DepthInches", out var dVal) && double.TryParse(dVal?.ToString(), out var parsedDepth))
            {
                depth = parsedDepth;
            }

            if (hasRebar && (hasWater || depth >= 4.0))
            {
                severityScore = "CRITICAL";
                riskCategory = "Immediate Structural Integrity Threat";
                aiRecommendation = $"Defect depth of {depth:F1}\" with exposed reinforcement steel and active water seepage presents immediate structural corrosion risk under load. Recommend immediate pour halt and structural lead intervention.";
                suggestedAssignee = "Lead Structural Engineer (P.E.)";
                recommendedStatus = form.Status == "Draft" ? "Submitted" : "UnderStructuralReview";
            }
            else if (depth >= 2.0 || hasRebar)
            {
                severityScore = "HIGH";
                riskCategory = "Major Concrete Spalling";
                aiRecommendation = $"Spalling exceeds 2\" threshold. Patch mortar repair specification needed prior to subsequent lift pour.";
                suggestedAssignee = "Senior QC Inspector";
                recommendedStatus = form.Status == "Draft" ? "Submitted" : "UnderStructuralReview";
            }
            else
            {
                severityScore = "LOW";
                riskCategory = "Cosmetic Surface Honeycombing";
                aiRecommendation = "Minor voiding within acceptable tolerances. Standard cosmetic grouting recommended.";
                suggestedAssignee = "General Superintendent";
            }
        }
        // Safety Hazard Incident Logic
        else if (form.FormId == "def-site-safety-hazard")
        {
            double trenchDepth = 0;
            if (form.Properties.TryGetValue("TrenchDepthFeet", out var tVal) && double.TryParse(tVal?.ToString(), out var parsedTrench))
            {
                trenchDepth = parsedTrench;
            }
            bool shoring = form.Properties.TryGetValue("ShoringPresent", out var shVal) && (shVal?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false);

            if (trenchDepth >= 5.0 && !shoring)
            {
                severityScore = "CRITICAL";
                riskCategory = "OSHA 1926 Subpart P Violation (Trench Collapse Hazard)";
                aiRecommendation = $"Excavation depth ({trenchDepth:F1} ft) exceeds OSHA 5-foot limit without protective shoring or trench box. Imminent cave-in danger. Immediate STOP-WORK order recommended.";
                suggestedAssignee = "Regional Safety Director";
                recommendedStatus = "SafetyHalt";
            }
            else
            {
                severityScore = "HIGH";
                riskCategory = "Site Hazard Observation";
                aiRecommendation = "Potential site safety hazard requires verification of barricades and warning signage.";
                suggestedAssignee = "Site Safety Coordinator";
                recommendedStatus = form.Status == "Draft" ? "Submitted" : "UnderSafetyReview";
            }
        }
    }

    private void ExtractViaDomainEngine(string raw, out string defId, out string defName, out Dictionary<string, object?> props, out double conf, out string summary, out string? rejectionReason)
    {
        props = new Dictionary<string, object?>();
        var lower = raw.ToLowerInvariant();
        rejectionReason = null;

        // Safety Hazard Match
        if (lower.Contains("trench") || lower.Contains("excavation") || lower.Contains("shoring") || lower.Contains("fall hazard") || lower.Contains("osha") || lower.Contains("scaffolding"))
        {
            defId = "def-site-safety-hazard";
            defName = "Site Safety Hazard Incident";
            
            // Dynamic Confidence calculation based on field coverage
            double calculatedConf = 0.65; // Base keyword match

            bool locationExtracted;
            props["Zone"] = ExtractLocation(raw, "Zone B - South Excavation", out locationExtracted);
            if (locationExtracted) calculatedConf += 0.15;

            props["HazardCategory"] = lower.Contains("trench") || lower.Contains("excavat") ? "Excavation" : "FallHazard";

            var depthMatch = Regex.Match(raw, @"(\d+(\.\d+)?)\s*(feet|foot|ft|')", RegexOptions.IgnoreCase);
            if (depthMatch.Success && double.TryParse(depthMatch.Groups[1].Value, out var ft))
            {
                props["TrenchDepthFeet"] = ft;
                calculatedConf += 0.10;
            }
            else
            {
                props["TrenchDepthFeet"] = 8.5; // Default reference estimate
            }

            props["ShoringPresent"] = !(lower.Contains("no shoring") || lower.Contains("missing shoring") || lower.Contains("without shoring"));
            if (lower.Contains("shoring")) calculatedConf += 0.05;

            var witnessMatch = Regex.Match(raw, @"(\d+)\s*(workers|people|personnel|witnesses)", RegexOptions.IgnoreCase);
            if (witnessMatch.Success && int.TryParse(witnessMatch.Groups[1].Value, out var wCount))
            {
                props["WitnessCount"] = wCount;
                calculatedConf += 0.05;
            }
            else
            {
                props["WitnessCount"] = 4;
            }

            props["ImmediateAction"] = lower.Contains("halt") || lower.Contains("stop") 
                ? "Issued immediate stop-work order and evacuated personnel from excavation zone."
                : "Notified site superintendent and barricaded perimeter.";

            conf = Math.Min(0.98, Math.Round(calculatedConf, 2));
            summary = $"Identified excavation hazard at '{props["Zone"]}'. Depth: {props["TrenchDepthFeet"]} ft, Shoring: {props["ShoringPresent"]}, Witnesses: {props["WitnessCount"]}.";
        }
        // Concrete Defect Match
        else if (lower.Contains("concrete") || lower.Contains("spall") || lower.Contains("crack") || lower.Contains("honeycomb") || lower.Contains("rebar") || lower.Contains("pour") || lower.Contains("footing") || lower.Contains("pier") || lower.Contains("void"))
        {
            defId = "def-concrete-quality";
            defName = "Concrete Structural Defect Report";
            
            double calculatedConf = 0.65; // Base keyword match

            bool locationExtracted;
            props["Location"] = ExtractLocation(raw, "Pier 4 East Footing, Station 12+40", out locationExtracted);
            if (locationExtracted) calculatedConf += 0.15;

            props["DefectType"] = lower.Contains("honeycomb") ? "Honeycombing" : lower.Contains("crack") ? "Crack" : "Spalling";
            props["Severity"] = (lower.Contains("severe") || lower.Contains("critical") || lower.Contains("halt")) ? "Critical" : "High";

            var depthMatch = Regex.Match(raw, @"(\d+(\.\d+)?)\s*(inch|inches|in|\"")", RegexOptions.IgnoreCase);
            if (depthMatch.Success && double.TryParse(depthMatch.Groups[1].Value, out var inch))
            {
                props["DepthInches"] = inch;
                calculatedConf += 0.10;
            }
            else
            {
                props["DepthInches"] = 5.0;
            }

            props["ExposedRebar"] = lower.Contains("rebar") || lower.Contains("reinforc") || lower.Contains("steel");
            props["WaterLeakage"] = lower.Contains("water") || lower.Contains("leak") || lower.Contains("moisture") || lower.Contains("seepage");
            if (props["ExposedRebar"] is true || props["WaterLeakage"] is true) calculatedConf += 0.08;

            props["InspectorNotes"] = raw.Trim();

            conf = Math.Min(0.98, Math.Round(calculatedConf, 2));
            summary = $"Extracted structural concrete defect at '{props["Location"]}'. Defect: {props["DefectType"]}, Depth: {props["DepthInches"]}\", Rebar: {props["ExposedRebar"]}, Moisture: {props["WaterLeakage"]}.";
        }
        // Out-of-Domain Rejection
        else
        {
            defId = "NONE";
            defName = "No Matching Form";
            conf = 0.20;
            summary = "Input does not contain recognized civil structural or safety inspection observations.";
            rejectionReason = "Note does not match any active civil engineering inspection forms (Concrete Quality, Site Safety Hazard).";
        }
    }

    private static string ExtractLocation(string raw, string fallback, out bool wasExtracted)
    {
        // 1. Check for prefix before colon: "Zone B South Excavation: ..." or "Pier 4 East Footing: ..."
        var prefixMatch = Regex.Match(raw, @"^([A-Za-z0-9\s\+\-\,\.]{3,40}?):");
        if (prefixMatch.Success && !string.IsNullOrWhiteSpace(prefixMatch.Groups[1].Value))
        {
            wasExtracted = true;
            return prefixMatch.Groups[1].Value.Trim();
        }

        // 2. Check for explicit Zone pattern: "Zone B South Excavation"
        var zoneMatch = Regex.Match(raw, @"\b(Zone\s+[A-Za-z0-9\-\s]{1,30}?)(?=:|\.|\,|\s+hazard|\s+trench|\s+is|$)", RegexOptions.IgnoreCase);
        if (zoneMatch.Success && !string.IsNullOrWhiteSpace(zoneMatch.Groups[1].Value))
        {
            wasExtracted = true;
            return zoneMatch.Groups[1].Value.Trim();
        }

        // 3. Check for explicit Pier/Structural pattern: "Pier 4 East Footing"
        var pierMatch = Regex.Match(raw, @"\b(Pier\s+[A-Za-z0-9\-\s\+\,]{1,35}?)(?=:|\.|\,|\s+found|\s+with|\s+and|$)", RegexOptions.IgnoreCase);
        if (pierMatch.Success && !string.IsNullOrWhiteSpace(pierMatch.Groups[1].Value))
        {
            wasExtracted = true;
            return pierMatch.Groups[1].Value.Trim();
        }

        // 4. Check for prepositions with strict word boundaries \b: "at Pier 4", "near South Abutment" (avoids matching "Rain is")
        var prepMatch = Regex.Match(raw, @"\b(at|near|on|in)\b\s+([A-Za-z0-9\s\+\-\,\.]{4,35}?)(?=\.|\,|\s+found|\s+with|\s+and|$)", RegexOptions.IgnoreCase);
        if (prepMatch.Success && !string.IsNullOrWhiteSpace(prepMatch.Groups[2].Value))
        {
            wasExtracted = true;
            return prepMatch.Groups[2].Value.Trim();
        }

        wasExtracted = false;
        return fallback;
    }

    private static object? ConvertJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        };
    }

    private async Task<(string MatchedDefId, string MatchedDefName, Dictionary<string, object?> Properties, double Confidence, string Summary)?> CallOpenAiExtractionAsync(string apiKey, string rawInput)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {apiKey}");

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new object[]
            {
                new { role = "system", content = "You are an infrastructure field inspection AI agent for Bentley Cloud Platform. Convert raw notes into structured JSON." },
                new { role = "user", content = $"Raw Note: {rawInput}" }
            },
            temperature = 0.1
        };

        req.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");
        var res = await _httpClient.SendAsync(req);
        if (!res.IsSuccessStatusCode) return null;

        return null;
    }

    private async Task<(string MatchedDefId, string MatchedDefName, Dictionary<string, object?> Properties, double Confidence, string Summary, string? RejectionReason)?> CallGroqExtractionAsync(
        string apiKey,
        string modelId,
        string rawInput,
        List<BentleyFormsService.Models.Entities.FormDefinitionEntity> availableDefinitions)
    {
        var endpoint = "https://api.groq.com/openai/v1/chat/completions";

        var defsBuilder = new System.Text.StringBuilder();
        if (availableDefinitions != null && availableDefinitions.Count > 0)
        {
            foreach (var d in availableDefinitions)
            {
                defsBuilder.AppendLine($"- Form ID: \"{d.Id}\" | Name: \"{d.DisplayName}\"");
                defsBuilder.AppendLine($"  Description: {d.Description}");
                defsBuilder.AppendLine("  Fields:");
                foreach (var f in d.Fields)
                {
                    var req = f.IsRequired ? "required" : "optional";
                    var opts = !string.IsNullOrWhiteSpace(f.OptionsJson) ? $", Options: {f.OptionsJson}" : "";
                    defsBuilder.AppendLine($"    * {f.Name} (type: {f.DataType}, {req}{opts})");
                }
                defsBuilder.AppendLine();
            }
        }

        var prompt = $$"""
You are an expert civil engineering AI agent for Bentley Cloud Platform (iTwin Forms).
Your role is to process unstructured voice notes, text messages, or inspector logs from job sites and map them to standard civil engineering form templates.

Available Project Form Templates:
{{defsBuilder}}

Special Rule for Out-of-Domain or Unrelated Inputs:
If the input note is NOT a civil engineering, construction, structural inspection, or site safety observation, you MUST return:
"matchedDefId": "NONE",
"matchedDefName": "No Matching Form",
"confidence": 0.15,
"summary": "Input does not relate to project inspection or safety operations.",
"properties": {},
"rejectionReason": "Specific reason why the input was rejected"

CRITICAL SCHEMA INTEGRITY RULES:
1. The "properties" dictionary MUST ONLY contain keys matching the EXACT field "Name" listed under the matched template in Available Project Form Templates above.
2. DO NOT invent or fabricate extra property keys (such as "WorkerCount", "DepthFeet", "EvacuationReason", "WaterLeakage", etc.). Use the exact field names (e.g. use "TrenchDepthFeet", not "DepthFeet"; use "WitnessCount", not "WorkerCount").
3. DO NOT carry over properties from other templates (e.g. NEVER put concrete fields like "DepthInches" or "WaterLeakage" in a Site Safety Hazard form).
4. Extract values strictly from the note. Do not output placeholder text.

Inspector Input Note:
"{{rawInput}}"

Output valid JSON matching this schema:
{
  "matchedDefId": "Exact Form ID (e.g. def-concrete-quality, def-site-safety-hazard, or NONE)",
  "matchedDefName": "Exact Form Name or 'No Matching Form'",
  "confidence": 0.95,
  "summary": "One-sentence factual summary of the extracted findings",
  "rejectionReason": "Only provide when matchedDefId is NONE",
  "properties": {
    "ExactFieldNameFromTemplate": "extracted value"
  }
}
""";

        var requestBody = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = "You are a civil engineering inspection AI agent that extracts structured form data and outputs strict JSON." },
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_object" },
            temperature = 0.1
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var res = await _httpClient.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Groq API returned status {StatusCode}: {ErrorBody}", res.StatusCode, errorBody);
                throw new HttpRequestException($"Groq API returned HTTP {(int)res.StatusCode} ({res.StatusCode})");
            }

            var responseJson = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var text = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var innerDoc = JsonDocument.Parse(text);
            var root = innerDoc.RootElement;

            string defId = root.TryGetProperty("matchedDefId", out var idProp) ? (idProp.GetString() ?? "NONE") : "NONE";
            string defName = root.TryGetProperty("matchedDefName", out var nameProp) ? (nameProp.GetString() ?? "No Matching Form") : "No Matching Form";
            double conf = root.TryGetProperty("confidence", out var cProp) ? cProp.GetDouble() : 0.95;
            string sum = root.TryGetProperty("summary", out var sProp) ? (sProp.GetString() ?? "") : "";
            string? rej = root.TryGetProperty("rejectionReason", out var rProp) ? rProp.GetString() : null;

            var props = new Dictionary<string, object?>();
            if (root.TryGetProperty("properties", out var propsElement) && propsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in propsElement.EnumerateObject())
                {
                    props[prop.Name] = ConvertJsonElement(prop.Value);
                }
            }

            return (defId, defName, props, conf, sum, rej);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Groq extraction timed out after 8s.");
            throw new TimeoutException("Groq API call timed out after 8 seconds.");
        }
    }

    private async Task<AiTriageResponseDto?> CallGroqTriageAsync(string apiKey, string modelId, FormResponseDto form)
    {
        var endpoint = "https://api.groq.com/openai/v1/chat/completions";

        var prompt = $$"""
You are a Senior Structural & Safety Engineering Lead triaging a field inspection for Bentley Cloud Platform (iTwin Forms).
Evaluate the form submission properties and determine the severity, risk category, actionable engineering recommendation, suggested assignee, and recommended next workflow status.

Workflow States Available:
- For def-concrete-quality: 'Draft', 'Submitted', 'UnderStructuralReview', 'Approved', 'Rejected', 'PendingLabTest'
- For def-site-safety-hazard: 'Draft', 'Submitted', 'UnderSafetyReview', 'SafetyHalt', 'Resolved'

Form Data Under Evaluation:
Form ID: {{form.Id}}
Form Definition: {{form.FormId}}
Current Status: {{form.Status}}
Submitted Properties:
{{JsonSerializer.Serialize(form.Properties, new JsonSerializerOptions { WriteIndented = true })}}

Output valid JSON matching this schema:
{
  "severityScore": "CRITICAL",
  "riskCategory": "OSHA 1926 Subpart P Violation",
  "aiRecommendation": "Detailed 2-3 sentence technical engineering analysis citing applicable standards (e.g. OSHA, ACI 318, IBC) and concrete repair/mitigation steps.",
  "suggestedAssignee": "Lead Structural Engineer (P.E.)",
  "recommendedStatus": "UnderStructuralReview"
}
""";

        var requestBody = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = "You are an expert civil structural & site safety engineering lead that outputs strict JSON." },
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_object" },
            temperature = 0.1
        };

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            req.Content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var res = await _httpClient.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode)
            {
                var errorBody = await res.Content.ReadAsStringAsync();
                _logger.LogWarning("Groq Triage API returned status {StatusCode}: {ErrorBody}", res.StatusCode, errorBody);
                throw new HttpRequestException($"Groq Triage API returned HTTP {(int)res.StatusCode} ({res.StatusCode})");
            }

            var responseJson = await res.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(responseJson);
            var choices = doc.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var text = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var innerDoc = JsonDocument.Parse(text);
            var root = innerDoc.RootElement;

            return new AiTriageResponseDto
            {
                FormId = form.Id,
                PreviousStatus = form.Status,
                SeverityScore = root.TryGetProperty("severityScore", out var sev) ? (sev.GetString() ?? "HIGH") : "HIGH",
                RiskCategory = root.TryGetProperty("riskCategory", out var cat) ? (cat.GetString() ?? "Engineering Review") : "Engineering Review",
                AiRecommendation = root.TryGetProperty("aiRecommendation", out var rec) ? (rec.GetString() ?? "") : "",
                SuggestedAssignee = root.TryGetProperty("suggestedAssignee", out var ass) ? (ass.GetString() ?? "Project Engineer") : "Project Engineer",
                RecommendedStatus = root.TryGetProperty("recommendedStatus", out var stat) ? (stat.GetString() ?? form.Status) : form.Status
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Groq triage timed out after 8s.");
            throw new TimeoutException("Groq Triage API call timed out after 8 seconds.");
        }
    }

    private Dictionary<string, object?> SanitizeAndMapProperties(
        Dictionary<string, object?> rawProperties,
        string matchedDefId,
        List<BentleyFormsService.Models.Entities.FormDefinitionEntity> availableDefinitions)
    {
        var sanitized = new Dictionary<string, object?>();
        var def = availableDefinitions?.FirstOrDefault(d => string.Equals(d.Id, matchedDefId, StringComparison.OrdinalIgnoreCase));
        if (def == null || def.Fields == null || def.Fields.Count == 0)
        {
            return rawProperties;
        }

        // Map common field name variations / synonyms to canonical schema names
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "depthfeet", "TrenchDepthFeet" },
            { "trenchdepth", "TrenchDepthFeet" },
            { "depth_feet", "TrenchDepthFeet" },
            { "depth", matchedDefId == "def-concrete-quality" ? "DepthInches" : "TrenchDepthFeet" },
            { "workercount", "WitnessCount" },
            { "workers", "WitnessCount" },
            { "worker_count", "WitnessCount" },
            { "witnesscount", "WitnessCount" },
            { "witnesses", "WitnessCount" },
            { "site", matchedDefId == "def-site-safety-hazard" ? "Zone" : "Location" },
            { "zones", "Zone" },
            { "mitigationaction", "ImmediateAction" },
            { "actiontaken", "ImmediateAction" },
            { "mitigation", "ImmediateAction" },
            { "shoring", "ShoringPresent" },
            { "shoring_present", "ShoringPresent" },
            { "rebar", "ExposedRebar" },
            { "rebar_exposed", "ExposedRebar" },
            { "leakage", "WaterLeakage" },
            { "notes", "InspectorNotes" }
        };

        var canonicalFields = def.Fields.ToDictionary(f => f.Name, f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in rawProperties)
        {
            var key = kvp.Key.Trim();
            var value = kvp.Value;

            // Strip out obvious prompt placeholder strings copied by the LLM
            if (value is string strVal && (
                strVal.Equals("Field notes here", StringComparison.OrdinalIgnoreCase) ||
                strVal.Equals("extracted value", StringComparison.OrdinalIgnoreCase) ||
                strVal.Equals("extracted value 123", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // 1. Direct match with canonical field
            if (canonicalFields.TryGetValue(key, out var canonicalField))
            {
                sanitized[canonicalField.Name] = CoerceValueToType(value, canonicalField.DataType);
            }
            // 2. Alias match
            else if (aliasMap.TryGetValue(key, out var mappedCanonicalName) && canonicalFields.TryGetValue(mappedCanonicalName, out canonicalField))
            {
                sanitized[canonicalField.Name] = CoerceValueToType(value, canonicalField.DataType);
            }
            // 3. Drop any invented/hallucinated fields not belonging to this schema!
            else
            {
                _logger.LogInformation("Sanitizer dropped unmapped/invented field '{Field}' with value '{Val}' for form template '{DefId}'", key, value, matchedDefId);
            }
        }

        return sanitized;
    }

    private static object? CoerceValueToType(object? value, string dataType)
    {
        if (value == null) return null;

        if (string.Equals(dataType, "number", StringComparison.OrdinalIgnoreCase))
        {
            if (value is double || value is int || value is long || value is float) return value;
            if (double.TryParse(value.ToString(), out var num)) return num;
        }
        else if (string.Equals(dataType, "boolean", StringComparison.OrdinalIgnoreCase))
        {
            if (value is bool) return value;
            if (bool.TryParse(value.ToString(), out var b)) return b;
            var str = value.ToString()?.Trim().ToLowerInvariant();
            if (str == "yes" || str == "true" || str == "1") return true;
            if (str == "no" || str == "false" || str == "0") return false;
        }

        return value;
    }
}

