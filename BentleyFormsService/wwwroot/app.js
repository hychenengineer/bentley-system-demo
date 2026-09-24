// Bentley Cloud Platform — AI Forms Service (Dual Persona Field & Office App)

const ITWIN_ID = "proj-hudson-tunnel-001";
let formDefinitions = [];
let allForms = [];
let activeForm = null;
let currentPersona = "field"; // "field" or "office"

document.addEventListener("DOMContentLoaded", () => {
  init();
});

async function init() {
  await loadFormDefinitions();
  await loadProjectForms();
}



// 1. Switch Top-Level Persona (Field Inspector vs Office Admin)
function switchPersona(persona) {
  currentPersona = persona;

  document.getElementById("btnPersonaField").classList.toggle("active", persona === "field");
  document.getElementById("btnPersonaOffice").classList.toggle("active", persona === "office");

  document.getElementById("viewField").classList.toggle("active", persona === "field");
  document.getElementById("viewOffice").classList.toggle("active", persona === "office");

  loadProjectForms();
}

// 2. Load Form Definitions
async function loadFormDefinitions() {
  try {
    const res = await fetch(`/forms/formDefinitions?iTwinId=${ITWIN_ID}`);
    if (!res.ok) throw new Error("Failed to load definitions");
    formDefinitions = await res.json();

    // 1. Populate Mode B (Manual Form Entry dropdown)
    const select = document.getElementById("selectFormDefinition");
    if (select) {
      select.innerHTML = '<option value="">-- Choose a Form Template --</option>';
      formDefinitions.forEach(def => {
        const opt = document.createElement("option");
        opt.value = def.id;
        opt.textContent = `${def.displayName} (${def.workflowType})`;
        select.appendChild(opt);
      });

      if (formDefinitions.length > 0) {
        select.value = formDefinitions[0].id;
        renderManualFormFields();
      }
    }

    // 2. Populate Mode A (AI Active Definitions banner)
    const bannerList = document.getElementById("aiActiveTemplatesList");
    const countBadge = document.getElementById("activeDefCountBadge");
    if (bannerList) {
      bannerList.innerHTML = "";
      if (countBadge) countBadge.textContent = `${formDefinitions.length} Schemas Injected`;

      formDefinitions.forEach(def => {
        const pill = document.createElement("div");
        pill.style.cssText = "background:#fff; border:1px solid #cbd5e1; border-radius:6px; padding:6px 12px; font-size:12px; display:inline-flex; align-items:center; gap:6px; box-shadow:0 1px 2px rgba(0,0,0,0.05);";
        const icon = def.id.includes("concrete") ? "🏗️" : "⚠️";
        pill.innerHTML = `<strong>${icon} ${escapeHtml(def.displayName)}</strong> <span style="color:#64748b; font-size:11px;">(${def.fields?.length || 0} fields • ${escapeHtml(def.workflowType)})</span>`;
        bannerList.appendChild(pill);
      });
    }
  } catch (err) {
    console.error("Error loading definitions:", err);
    const countBadge = document.getElementById("activeDefCountBadge");
    if (countBadge) countBadge.textContent = "Error loading";
  }
}

// 3. Load Project Forms
async function loadProjectForms() {
  const fieldTbody = document.getElementById("fieldFormsTableBody");
  const officeTbody = document.getElementById("officeFormsTableBody");

  try {
    const res = await fetch(`/forms?iTwinId=${ITWIN_ID}&$top=50`);
    if (!res.ok) throw new Error("Failed to load forms");
    allForms = await res.json();

    renderFieldTable(fieldTbody);
    renderOfficeTable(officeTbody);
  } catch (err) {
    if (fieldTbody) fieldTbody.innerHTML = `<tr><td colspan="7" class="error-banner">Error loading forms: ${err.message}</td></tr>`;
    if (officeTbody) officeTbody.innerHTML = `<tr><td colspan="7" class="error-banner">Error loading queue: ${err.message}</td></tr>`;
  }
}

function renderFieldTable(tbody) {
  if (!tbody) return;

  // Field Inspector view: ONLY display forms under "Draft" and "Submitted" status!
  const fieldForms = allForms.filter(f => {
    const s = f.status.toLowerCase();
    return s === "draft" || s === "submitted";
  });

  if (fieldForms.length === 0) {
    tbody.innerHTML = '<tr><td colspan="7" class="table-loading">No active draft or submitted inspections. Create one above!</td></tr>';
    return;
  }

  tbody.innerHTML = "";
  fieldForms.forEach(form => {
    const tr = document.createElement("tr");
    const def = formDefinitions.find(d => d.id === form.formId);
    const defName = def ? def.displayName : form.formId;
    const dateStr = formatTime(form.createdDateTime);
    const statusClass = `status-${form.status.toLowerCase().replace(/[^a-z]/g, '')}`;

    const isDraft = form.status.toLowerCase() === "draft";

    tr.innerHTML = `
      <td><a href="#" class="form-id-link" onclick="openDetailModal('${form.id}'); return false;">${form.id}</a></td>
      <td><strong>${escapeHtml(defName)}</strong></td>
      <td>${escapeHtml(form.subject)}</td>
      <td><span class="status-pill ${statusClass}">${escapeHtml(form.status)}</span></td>
      <td>${form.aiExtracted ? `<span class="badge badge-ai">🤖 AI (${Math.round((form.aiConfidence||0.9)*100)}%)</span>` : `<span class="prop-key">✍️ Manual</span>`}</td>
      <td><small class="prop-key">${dateStr}</small></td>
      <td>
        ${isDraft 
          ? `<button class="btn btn-primary btn-sm" onclick="openDetailModal('${form.id}')">✏️ Edit Draft & Submit</button>` 
          : `<button class="btn btn-secondary btn-sm" onclick="openDetailModal('${form.id}')">👁️ View (Locked)</button>`}
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function renderOfficeTable(tbody) {
  if (!tbody) return;

  // Office Review Queue: Exclude incomplete "Draft" forms!
  // Only show tickets that have been officially submitted or are in active engineering review/sign-off
  const officeForms = allForms.filter(f => f.status.toLowerCase() !== "draft");

  if (officeForms.length === 0) {
    tbody.innerHTML = '<tr><td colspan="7" class="table-loading">Queue is clear. No submitted forms waiting for review.</td></tr>';
    return;
  }

  tbody.innerHTML = "";
  officeForms.forEach(form => {
    const tr = document.createElement("tr");
    const def = formDefinitions.find(d => d.id === form.formId);
    const defName = def ? def.displayName : form.formId;
    const dateStr = formatTime(form.createdDateTime);
    const statusClass = `status-${form.status.toLowerCase().replace(/[^a-z]/g, '')}`;

    tr.innerHTML = `
      <td><a href="#" class="form-id-link" onclick="openDetailModal('${form.id}'); return false;">${form.id}</a></td>
      <td><strong>${escapeHtml(defName)}</strong></td>
      <td>${escapeHtml(form.subject)}</td>
      <td><span class="status-pill ${statusClass}">${escapeHtml(form.status)}</span></td>
      <td>${form.aiExtracted ? `<span class="badge badge-ai">🤖 AI</span>` : `<span class="prop-key">✍️ Manual</span>`}</td>
      <td><small class="prop-key">${dateStr}</small></td>
      <td>
        <button class="btn btn-warning btn-sm" onclick="openDetailModal('${form.id}')">🛡️ Review & AI Triage</button>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function formatTime(iso) {
  return new Date(iso).toLocaleString(undefined, {
    month: "short", day: "numeric", hour: "2-digit", minute: "2-digit"
  });
}

// 4. Tab Switching inside Field View
function switchIntakeTab(mode) {
  document.getElementById("tabAiBtn").classList.toggle("active", mode === "ai");
  document.getElementById("tabManualBtn").classList.toggle("active", mode === "manual");
  document.getElementById("aiIntakePanel").classList.toggle("active", mode === "ai");
  document.getElementById("manualIntakePanel").classList.toggle("active", mode === "manual");
}

// 5. Sample Prompts for AI Field Mode
function loadSamplePrompt(type) {
  const input = document.getElementById("aiInputText");
  if (type === "concrete") {
    input.value = "Pier 4 East Footing, station 12+40: Found severe honeycomb spalling, roughly 5.5 inches deep, with exposed rusting rebar and active moisture seepage. Halted concrete pouring on Bay 2 immediately until structural review.";
  } else if (type === "excavation") {
    input.value = "Zone B South Excavation: Trench depth is currently 9 feet deep without any protective shoring or trench boxes installed. Rain is expected tonight. Issued immediate stop-work order and evacuated 4 workers from the excavation pit.";
  }
}

// 6. Mode A: AI-Assisted Extract & Create (Field Mode)
async function handleAiExtractAndCreate() {
  const text = document.getElementById("aiInputText").value.trim();
  const statusEl = document.getElementById("aiStatusText");
  const btn = document.getElementById("btnExtractAi");
  const resultCard = document.getElementById("aiExtractionResultCard");

  if (!text) {
    alert("Please enter or paste a field note first.");
    return;
  }

  btn.disabled = true;
  statusEl.textContent = "AI Agent extracting fields...";
  resultCard.classList.add("hidden");

  const abortCtrl = new AbortController();
  const timeoutTimer = setTimeout(() => abortCtrl.abort(), 8000);

  try {
    const enableFallback = localStorage.getItem("enable_fallback") === "true";
    const selectedModel = localStorage.getItem("preferred_ai_model") || "openai/gpt-oss-120b";
    
    let activeApiKey = null;
    if (selectedModel.startsWith("openai/") || selectedModel.startsWith("qwen/") || selectedModel.startsWith("allam") || selectedModel.startsWith("meta-llama/")) {
      activeApiKey = localStorage.getItem("groq_api_key");
    } else if (selectedModel.startsWith("gemini-")) {
      activeApiKey = localStorage.getItem("google_api_key");
    }

    if (!activeApiKey && !enableFallback) {
      statusEl.innerHTML = `<span style="color:#d9534f; font-weight:600;">⚠️ API Key Missing: You need an API key from Groq or Google Gemini in <a href="javascript:void(0)" onclick="openSettingsModal()" style="text-decoration:underline;">⚙️ Settings</a>, or please turn on <em>"Enable Fallback to Heuristic Engine"</em> under Settings.</span>`;
      btn.disabled = false;
      clearTimeout(timeoutTimer);
      return;
    }

    const res = await fetch("/forms/ai/extract-and-create", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        iTwinId: ITWIN_ID,
        rawInput: text,
        enableFallback: enableFallback,
        modelId: selectedModel,
        apiKey: activeApiKey
      }),
      signal: abortCtrl.signal
    });
    clearTimeout(timeoutTimer);

    const data = await res.json();
    if (!res.ok) throw new Error(data.error || "Failed to extract form");

    // Guardrail Check: AI rejected out-of-domain input or LLM failure when fallback disabled
    if (data.success === false || data.matchedDefinitionId === "NONE") {
      resultCard.classList.add("hidden");
      statusEl.innerHTML = `<span style="color:#d9534f; font-weight:600;">⚠️ ${escapeHtml(data.rejectionReason || data.summary || "Input not recognized as a civil inspection observation.")}</span>`;
      return;
    }

    statusEl.textContent = "Draft form auto-created in field mode!";
    setTimeout(() => { statusEl.textContent = ""; }, 3000);

    // Display extraction card
    document.getElementById("aiMatchedDefName").textContent = data.matchedDefinitionName;
    document.getElementById("aiConfidenceBadge").textContent = `Confidence: ${Math.round(data.confidence * 100)}%`;
    
    const modelBadge = document.getElementById("aiModelBadge");
    if (modelBadge) {
      modelBadge.textContent = data.modelUsed ? `Model: ${data.modelUsed}` : `Model: ${selectedModel}`;
    }

    document.getElementById("aiCreatedFormId").textContent = `ID: ${data.formId}`;
    document.getElementById("aiSummaryText").textContent = data.summary;

    const propsGrid = document.getElementById("aiExtractedPropsGrid");
    propsGrid.innerHTML = "";
    for (const [key, val] of Object.entries(data.properties)) {
      const item = document.createElement("div");
      item.className = "prop-pill";
      item.innerHTML = `<span class="prop-key">${escapeHtml(key)}:</span> <span class="prop-val">${escapeHtml(String(val))}</span>`;
      propsGrid.appendChild(item);
    }
    resultCard.classList.remove("hidden");

    await loadProjectForms();
  } catch (err) {
    clearTimeout(timeoutTimer);
    if (err.name === 'AbortError') {
      statusEl.innerHTML = `<span style="color:#d9534f; font-weight:600;">⚠️ Request timed out after 8s (LLM did not respond).</span>`;
    } else {
      statusEl.textContent = `Error: ${err.message}`;
    }
  } finally {
    btn.disabled = false;
  }
}

// 7. Mode B: Manual Form Generation (Field Mode)
function renderManualFormFields() {
  const select = document.getElementById("selectFormDefinition");
  const container = document.getElementById("manualFormContainer");
  const defId = select.value;

  if (!defId) {
    container.innerHTML = '<div class="loading-placeholder">Select a template to generate form fields...</div>';
    return;
  }

  const def = formDefinitions.find(d => d.id === defId);
  if (!def) return;

  container.innerHTML = "";

  const subjectGroup = document.createElement("div");
  subjectGroup.className = "form-group form-field-full";
  subjectGroup.innerHTML = `
    <label>Form Subject / Title *</label>
    <input type="text" id="manual_subject" class="form-control" required value="${escapeHtml(def.displayName)} - Field Observation" />
  `;
  container.appendChild(subjectGroup);

  def.fields.forEach(f => {
    const group = document.createElement("div");
    group.className = f.dataType === "string" && f.name.toLowerCase().includes("notes") ? "form-group form-field-full" : "form-group";

    let inputHtml = "";
    if (f.dataType === "select" && f.options) {
      const opts = f.options.map(o => `<option value="${escapeHtml(o)}">${escapeHtml(o)}</option>`).join("");
      inputHtml = `<select id="field_${f.Name || f.name}" class="form-control" ${f.isRequired ? 'required' : ''}>${opts}</select>`;
    } else if (f.dataType === "boolean") {
      inputHtml = `
        <select id="field_${f.Name || f.name}" class="form-control">
          <option value="true">Yes / True</option>
          <option value="false">No / False</option>
        </select>
      `;
    } else if (f.dataType === "number") {
      inputHtml = `<input type="number" step="any" id="field_${f.Name || f.name}" class="form-control" ${f.isRequired ? 'required' : ''} placeholder="0.0" />`;
    } else {
      inputHtml = `<input type="text" id="field_${f.Name || f.name}" class="form-control" ${f.isRequired ? 'required' : ''} placeholder="Enter ${escapeHtml(f.displayName)}" />`;
    }

    group.innerHTML = `<label>${escapeHtml(f.displayName)} ${f.isRequired ? '*' : ''}</label>${inputHtml}`;
    container.appendChild(group);
  });

  const submitRow = document.createElement("div");
  submitRow.className = "form-submit-row";
  submitRow.innerHTML = `
    <button type="submit" class="btn btn-primary">
      <span>💾</span> Save Draft Form (POST /forms)
    </button>
    <span id="manualFormStatus" class="status-msg"></span>
  `;
  container.appendChild(submitRow);
}

async function handleManualFormSubmit(e) {
  e.preventDefault();
  const select = document.getElementById("selectFormDefinition");
  const defId = select.value;
  const def = formDefinitions.find(d => d.id === defId);
  const statusEl = document.getElementById("manualFormStatus");

  if (!def) return;

  const subject = document.getElementById("manual_subject").value.trim();
  const properties = {};

  def.fields.forEach(f => {
    const el = document.getElementById(`field_${f.Name || f.name}`);
    if (el) {
      if (f.dataType === "boolean") {
        properties[f.name] = el.value === "true";
      } else if (f.dataType === "number") {
        properties[f.name] = el.value ? parseFloat(el.value) : 0;
      } else {
        properties[f.name] = el.value;
      }
    }
  });

  statusEl.textContent = "Saving draft form...";

  try {
    const res = await fetch("/forms", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        iTwinId: ITWIN_ID,
        formId: defId,
        subject: subject,
        status: "Draft",
        properties: properties
      })
    });

    const data = await res.json();
    if (!res.ok) throw new Error(data.error || "Failed to create form");

    statusEl.textContent = `Draft created (ID: ${data.id})!`;
    setTimeout(() => { statusEl.textContent = ""; }, 3000);
    await loadProjectForms();
  } catch (err) {
    statusEl.textContent = `Error: ${err.message}`;
  }
}

// 8. Form Details Modal & State Machine (Adaptive to Persona)
async function openDetailModal(formId) {
  const modal = document.getElementById("formDetailModal");
  modal.classList.remove("hidden");
  document.getElementById("transitionErrorBox").classList.add("hidden");
  document.getElementById("triageResultBox").classList.add("hidden");

  try {
    const res = await fetch(`/forms/${formId}`);
    if (!res.ok) throw new Error("Failed to load form details");
    activeForm = await res.json();

    document.getElementById("modalSubject").textContent = activeForm.subject;
    document.getElementById("modalMeta").textContent = `ID: ${activeForm.id} • Status: ${activeForm.status} • Project: ${activeForm.iTwinId}`;

    const def = formDefinitions.find(d => d.id === activeForm.formId);
    let workflow = null;
    if (def) {
      const wfRes = await fetch(`/forms/workflows/${def.workflowType}`);
      if (wfRes.ok) {
        workflow = await wfRes.json();
        renderWorkflowStageBar(workflow, activeForm.status);
      }
    }

    const isDraft = activeForm.status.toLowerCase() === "draft";
    const canEdit = isDraft && currentPersona === "field";

    renderPropertiesViewer(activeForm.properties, canEdit, def);
    renderAuditTimeline(activeForm.auditLogs);

    // PERSONA ROLE BOUNDARY LOGIC
    const triagePanel = document.getElementById("modalTriagePanel");
    const lockedNotice = document.getElementById("modalFieldLockedNotice");
    const transitionSectionTitle = document.getElementById("transitionSectionTitle");
    const transitionSectionHint = document.getElementById("transitionSectionHint");
    const transitionRow = document.getElementById("modalTransitionButtons");

    transitionRow.innerHTML = "";

    if (currentPersona === "field") {
      // FIELD VIEW: No AI Triage button!
      triagePanel.classList.add("hidden");

      if (isDraft) {
        lockedNotice.classList.add("hidden");
        transitionSectionTitle.textContent = "Field Actions: Save & Submit";
        transitionSectionHint.textContent = "You can modify draft values above, or submit to the engineering office when ready.";
        
        const saveDraftBtn = document.createElement("button");
        saveDraftBtn.className = "btn btn-secondary";
        saveDraftBtn.innerHTML = `💾 Save Modifications (PATCH /forms)`;
        saveDraftBtn.onclick = handleSaveDraftModifications;
        transitionRow.appendChild(saveDraftBtn);

        const submitBtn = document.createElement("button");
        submitBtn.className = "btn btn-primary";
        submitBtn.innerHTML = `🚀 Submit Inspection (Move to 'Submitted')`;
        submitBtn.onclick = () => handleStateTransition("Submitted");
        transitionRow.appendChild(submitBtn);
      } else {
        // Form is Submitted or beyond -> Locked for field worker!
        lockedNotice.classList.remove("hidden");
        transitionSectionTitle.textContent = "Form Lifecycle (Read-Only)";
        transitionSectionHint.textContent = "Field edits and state changes are restricted once submitted.";
        transitionRow.innerHTML = `<span class="prop-key">No actions available for Field Inspector in state '${escapeHtml(activeForm.status)}'.</span>`;
      }
    } else {
      // OFFICE ADMIN VIEW: Show AI Triage!
      triagePanel.classList.remove("hidden");
      lockedNotice.classList.add("hidden");
      transitionSectionTitle.textContent = "Engineering State Machine Actions";
      transitionSectionHint.textContent = "Authorized transitions governed by the state machine.";

      if (workflow) {
        const stateDetails = workflow.states[activeForm.status];
        const allowed = stateDetails ? stateDetails.allowedTransitions : [];

        if (allowed.length === 0) {
          transitionRow.innerHTML = `<span class="prop-key">Terminal State reached ('${escapeHtml(activeForm.status)}'). No further transitions allowed.</span>`;
        } else {
          allowed.forEach(target => {
            const btn = document.createElement("button");
            btn.className = target.toLowerCase().includes("approved") || target.toLowerCase().includes("resolved") ? "btn btn-success btn-sm" : "btn btn-primary btn-sm";
            btn.innerHTML = `Advance to: <strong>${escapeHtml(target)}</strong>`;
            btn.onclick = () => handleStateTransition(target);
            transitionRow.appendChild(btn);
          });
        }
      }
    }

  } catch (err) {
    alert("Error loading form details: " + err.message);
  }
}

function closeDetailModal() {
  document.getElementById("formDetailModal").classList.add("hidden");
  activeForm = null;
}

function renderWorkflowStageBar(workflow, currentStatus) {
  const bar = document.getElementById("modalWorkflowStageBar");
  bar.innerHTML = "";

  const states = Object.keys(workflow.states);
  states.forEach((st, idx) => {
    const step = document.createElement("div");
    step.className = `stage-step ${st.toLowerCase() === currentStatus.toLowerCase() ? 'active' : ''}`;
    step.innerHTML = `<span>${st.toLowerCase() === currentStatus.toLowerCase() ? '●' : '○'}</span> <span>${escapeHtml(st)}</span>`;
    bar.appendChild(step);

    if (idx < states.length - 1) {
      const arrow = document.createElement("span");
      arrow.className = "stage-arrow";
      arrow.textContent = "→";
      bar.appendChild(arrow);
    }
  });
}

// 9. Modify Instance in Draft Mode (PATCH /forms/{id})
async function handleSaveDraftModifications() {
  if (!activeForm) return;
  const def = formDefinitions.find(d => d.id === activeForm.formId);
  const updatedProps = {};

  if (def) {
    def.fields.forEach(f => {
      const input = document.getElementById(`modal_edit_${f.Name || f.name}`);
      if (input) {
        if (f.dataType === "boolean") {
          updatedProps[f.name] = input.value === "true";
        } else if (f.dataType === "number") {
          updatedProps[f.name] = input.value ? parseFloat(input.value) : 0;
        } else {
          updatedProps[f.name] = input.value;
        }
      }
    });
  }

  try {
    const res = await fetch(`/forms/${activeForm.id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        properties: updatedProps,
        workflowNote: "Field Inspector modified draft values prior to submission."
      })
    });

    const data = await res.json();
    if (!res.ok) throw new Error(data.error || "Failed to update draft");

    alert("Draft changes saved successfully!");
    await openDetailModal(activeForm.id);
    await loadProjectForms();
  } catch (err) {
    alert("Save error: " + err.message);
  }
}

async function handleStateTransition(targetStatus) {
  if (!activeForm) return;
  const errorBox = document.getElementById("transitionErrorBox");
  errorBox.classList.add("hidden");

  try {
    const res = await fetch(`/forms/${activeForm.id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        status: targetStatus,
        workflowNote: `${currentPersona === "field" ? "Field Inspector" : "Office Lead"} transitioned state to ${targetStatus}`
      })
    });

    const data = await res.json();
    if (!res.ok) {
      errorBox.textContent = `State Machine Rejection: ${data.error}`;
      errorBox.classList.remove("hidden");
      return;
    }

    await openDetailModal(activeForm.id);
    await loadProjectForms();
  } catch (err) {
    errorBox.textContent = "Network error: " + err.message;
    errorBox.classList.remove("hidden");
  }
}

// 10. AI Triage Execution (OFFICE ADMIN ONLY)
async function handleRunAiTriage() {
  if (!activeForm) return;
  const btn = document.getElementById("btnRunTriage");
  const triageBox = document.getElementById("triageResultBox");

  btn.disabled = true;
  btn.textContent = "Evaluating Risk via AI...";

  try {
    const enableFallback = localStorage.getItem("enable_fallback") === "true";
    const triageModel = localStorage.getItem("preferred_ai_model") || "openai/gpt-oss-120b";
    
    let activeApiKey = null;
    if (triageModel.startsWith("openai/") || triageModel.startsWith("qwen/") || triageModel.startsWith("allam") || triageModel.startsWith("meta-llama/")) {
      activeApiKey = localStorage.getItem("groq_api_key");
    } else if (triageModel.startsWith("gemini-")) {
      activeApiKey = localStorage.getItem("google_api_key");
    }

    if (!activeApiKey && !enableFallback) {
      alert("⚠️ API Key Missing: You need an API key from either Groq or Google Gemini in ⚙️ Settings, or please turn on 'Enable Fallback to Heuristic Engine' under Settings to test offline.");
      btn.disabled = false;
      btn.textContent = "⚡ Run AI Triage";
      return;
    }

    const res = await fetch(`/forms/${activeForm.id}/ai/triage`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        applyTransition: false,
        triageRole: "LeadSafetyOfficer",
        modelId: triageModel,
        enableFallback: enableFallback,
        apiKey: activeApiKey
      })
    });

    const data = await res.json();
    if (!res.ok) throw new Error(data.error || "Triage failed");

    window.lastTriageData = data;

    triageBox.innerHTML = `
      <div class="triage-badge-row">
        <span class="badge-severity severity-${escapeHtml(data.severityScore)}">${escapeHtml(data.severityScore)} SEVERITY</span>
        <strong>${escapeHtml(data.riskCategory)}</strong>
        <span class="badge" style="background:#e8f0fe; color:#1a73e8; border:1px solid #c2e7ff; font-size:11px; margin-left:auto;">Model: ${escapeHtml(triageModel)}</span>
      </div>
      <p class="triage-rec-text">${escapeHtml(data.aiRecommendation)}</p>
      <div style="display:flex; justify-content:space-between; align-items:center;">
        <small class="prop-key">Recommended Next State: <strong>${escapeHtml(data.recommendedStatus)}</strong> • Assigned to: ${escapeHtml(data.suggestedAssignee)}</small>
        <button class="btn btn-success btn-sm" onclick="applyLastTriageTransition()">
          ✓ Apply Recommended Escalation
        </button>
      </div>
    `;
    triageBox.classList.remove("hidden");
  } catch (err) {
    alert("Triage error: " + err.message);
  } finally {
    btn.disabled = false;
    btn.textContent = "⚡ Run AI Triage";
  }
}

async function applyLastTriageTransition() {
  if (!activeForm || !window.lastTriageData) return;
  await applyTriageTransition(window.lastTriageData.recommendedStatus, window.lastTriageData.aiRecommendation);
}

async function applyTriageTransition(targetStatus, note) {
  if (!activeForm) return;
  try {
    const res = await fetch(`/forms/${activeForm.id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        status: targetStatus,
        workflowNote: `[AI Automated Escalation]: ${note}`
      })
    });

    if (res.ok) {
      await openDetailModal(activeForm.id);
      await loadProjectForms();
    } else {
      const errorData = await res.json();
      alert("State Machine Rejection: " + (errorData.error || "Unknown error"));
    }
  } catch (err) {
    alert("Transition error: " + err.message);
  }
}

function renderPropertiesViewer(properties, canEdit, def) {
  const viewer = document.getElementById("modalPropertiesViewer");
  viewer.innerHTML = "";

  for (const [k, v] of Object.entries(properties)) {
    const fieldDef = def ? def.fields.find(f => f.name.toLowerCase() === k.toLowerCase()) : null;
    const item = document.createElement("div");
    item.className = "prop-item";

    if (canEdit && fieldDef) {
      // RENDER EDITABLE INPUTS FOR DRAFT MODE
      let inputHtml = "";
      if (fieldDef.dataType === "select" && fieldDef.options) {
        const opts = fieldDef.options.map(o => `<option value="${escapeHtml(o)}" ${String(v) === o ? 'selected' : ''}>${escapeHtml(o)}</option>`).join("");
        inputHtml = `<select id="modal_edit_${fieldDef.Name || fieldDef.name}" class="form-control" style="font-size:0.85rem; padding:6px 10px;">${opts}</select>`;
      } else if (fieldDef.dataType === "boolean") {
        inputHtml = `
          <select id="modal_edit_${fieldDef.Name || fieldDef.name}" class="form-control" style="font-size:0.85rem; padding:6px 10px;">
            <option value="true" ${v === true || v === "true" ? 'selected' : ''}>Yes / True</option>
            <option value="false" ${v === false || v === "false" ? 'selected' : ''}>No / False</option>
          </select>
        `;
      } else if (fieldDef.dataType === "number") {
        inputHtml = `<input type="number" step="any" id="modal_edit_${fieldDef.Name || fieldDef.name}" class="form-control" value="${escapeHtml(String(v))}" style="font-size:0.85rem; padding:6px 10px;" />`;
      } else {
        inputHtml = `<input type="text" id="modal_edit_${fieldDef.Name || fieldDef.name}" class="form-control" value="${escapeHtml(String(v))}" style="font-size:0.85rem; padding:6px 10px;" />`;
      }

      item.innerHTML = `
        <div class="prop-key" style="margin-bottom:4px;">${escapeHtml(fieldDef.displayName || k)} ✏️</div>
        ${inputHtml}
      `;
    } else {
      // RENDER LOCKED READ-ONLY LABELS FOR SUBMITTED / CLOSED MODES
      item.innerHTML = `
        <div class="prop-key">${escapeHtml(fieldDef ? fieldDef.displayName : k)} 🔒</div>
        <div class="prop-val">${escapeHtml(String(v))}</div>
      `;
    }

    viewer.appendChild(item);
  }
}

function renderAuditTimeline(logs) {
  const timeline = document.getElementById("modalAuditTimeline");
  timeline.innerHTML = "";

  if (!logs || logs.length === 0) {
    timeline.innerHTML = '<span class="prop-key">No audit records yet.</span>';
    return;
  }

  logs.forEach(log => {
    const entry = document.createElement("div");
    entry.className = "audit-entry";
    const timeStr = new Date(log.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    entry.innerHTML = `
      <div class="audit-meta"><strong>${escapeHtml(log.action)}</strong> • ${timeStr} by <em>${escapeHtml(log.actor)}</em></div>
      <div>${escapeHtml(log.notes || '')}</div>
    `;
    timeline.appendChild(entry);
  });
}

function escapeHtml(str) {
  if (!str) return "";
  return String(str)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

async function handleCleanAllTickets() {
  if (!confirm("Are you sure you want to delete ALL tickets in this project? This cannot be undone.")) return;
  try {
    const res = await fetch(`/forms/clean-all?iTwinId=${encodeURIComponent(ITWIN_ID)}`, {
      method: "DELETE"
    });
    const data = await res.json();
    if (res.ok) {
      alert(data.message || "All tickets cleaned successfully.");
      await loadProjectForms();
    } else {
      alert("Error cleaning tickets: " + data.error);
    }
  } catch (err) {
    alert("Network error while cleaning tickets: " + err.message);
  }
}


// --- Settings Modal Logic ---
function openSettingsModal() {
  document.getElementById('settingGroqKey').value = localStorage.getItem('groq_api_key') || '';
  document.getElementById('settingGoogleKey').value = localStorage.getItem('google_api_key') || '';
  document.getElementById('settingDefaultModel').value = localStorage.getItem('preferred_ai_model') || 'openai/gpt-oss-120b';
  document.getElementById('settingEnableFallback').checked = localStorage.getItem('enable_fallback') === 'true';
  
  document.getElementById('settingsModal').classList.remove('hidden');
}

function closeSettingsModal() {
  document.getElementById('settingsModal').classList.add('hidden');
}

function saveSettings() {
  localStorage.setItem('groq_api_key', document.getElementById('settingGroqKey').value.trim());
  localStorage.setItem('google_api_key', document.getElementById('settingGoogleKey').value.trim());
  localStorage.setItem('preferred_ai_model', document.getElementById('settingDefaultModel').value);
  localStorage.setItem('enable_fallback', document.getElementById('settingEnableFallback').checked ? 'true' : 'false');
  
  closeSettingsModal();
}


// --- Voice Dictation Logic ---
let recognition = null;
let isDictating = false;

function toggleDictation() {
  const btn = document.getElementById('btnDictate');
  const input = document.getElementById('aiInputText');
  
  if (isDictating) {
    if (recognition) recognition.stop();
    return;
  }
  
  if (!('webkitSpeechRecognition' in window) && !('SpeechRecognition' in window)) {
    alert('Speech recognition is not supported in this browser. Please use Chrome or Edge.');
    return;
  }
  
  const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
  recognition = new SpeechRecognition();
  recognition.continuous = true;
  recognition.interimResults = true;
  recognition.lang = 'en-US';
  
  recognition.onstart = function() {
    isDictating = true;
    btn.textContent = '🔴';
    btn.style.color = 'red';
  };
  
  let finalTranscript = input.value;
  
  recognition.onresult = function(event) {
    let interimTranscript = '';
    
    for (let i = event.resultIndex; i < event.results.length; ++i) {
      if (event.results[i].isFinal) {
        finalTranscript += event.results[i][0].transcript;
      } else {
        interimTranscript += event.results[i][0].transcript;
      }
    }
    input.value = finalTranscript + interimTranscript;
  };
  
  recognition.onerror = function(event) {
    console.error('Speech recognition error', event.error);
    if (event.error !== 'no-speech') {
      alert('Speech recognition error: ' + event.error);
    }
  };
  
  recognition.onend = function() {
    isDictating = false;
    btn.textContent = '🎤';
    btn.style.color = '#6c757d';
  };
  
  recognition.start();
}

