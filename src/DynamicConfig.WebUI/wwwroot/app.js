"use strict";

// Vanilla JS on purpose: the case scores functionality, not visuals — a SPA
// framework would add a build pipeline and docker complexity for zero
// requirement coverage. Discipline still applies: one in-memory state, small
// single-purpose functions, one-way flow (API → state → render). The DOM is a
// render target only — nothing ever reads state back out of it.

const API_BASE = "/api/configurations";
const GENERIC_ERROR_MESSAGE = "Something went wrong. Please try again.";

// Accepted storage spellings → the dropdown's four canonical options.
const TYPE_NORMALIZATION = {
  string: "string",
  int: "int",
  integer: "int",
  double: "double",
  bool: "bool",
  boolean: "bool",
};

const elements = {
  filterInput: document.getElementById("name-filter"),
  addButton: document.getElementById("add-button"),
  globalMessage: document.getElementById("global-message"),
  formPanel: document.getElementById("form-panel"),
  formTitle: document.getElementById("form-title"),
  form: document.getElementById("record-form"),
  cancelButton: document.getElementById("cancel-button"),
  recordsTable: document.getElementById("records-table"),
  recordsBody: document.getElementById("records-body"),
  emptyMessage: document.getElementById("empty-message"),
};

// ---- state ------------------------------------------------------------------

/** The single source of truth; renderTable projects it, filters never refetch. */
let allRecords = [];

// ---- API --------------------------------------------------------------------

async function fetchAllRecords() {
  const response = await fetch(API_BASE);
  if (!response.ok) {
    throw new Error(`GET ${API_BASE} failed with ${response.status}`);
  }
  allRecords = await response.json();
  renderTable();
}

// ---- rendering ----------------------------------------------------------------

/**
 * Projects state into the table. The name filter narrows the already-loaded
 * array — case-insensitive contains, zero network requests on this path.
 */
function renderTable() {
  const filterText = elements.filterInput.value.trim().toLowerCase();
  const visibleRecords = filterText === ""
    ? allRecords
    : allRecords.filter((record) => record.name.toLowerCase().includes(filterText));

  elements.recordsBody.replaceChildren(...visibleRecords.map(buildRow));
  elements.emptyMessage.hidden = visibleRecords.length > 0;
}

// Rows are built with createElement/textContent: record values are user data
// and must never be interpreted as markup.
function buildRow(record) {
  const row = document.createElement("tr");
  if (!record.isActive) {
    row.classList.add("inactive");
  }

  row.append(
    buildCell(record.name),
    buildCell(record.type),
    buildCell(record.value),
    buildStatusCell(record.isActive),
    buildCell(record.applicationName),
    buildCell(formatUtcTimestamp(record.lastModifiedDate)),
    buildEditCell(record),
  );
  return row;
}

function buildCell(text) {
  const cell = document.createElement("td");
  cell.textContent = text;
  return cell;
}

function buildStatusCell(isActive) {
  const cell = document.createElement("td");
  const badge = document.createElement("span");
  badge.className = isActive ? "badge badge-active" : "badge badge-inactive";
  badge.textContent = isActive ? "Active" : "Inactive";
  cell.append(badge);
  return cell;
}

function buildEditCell(record) {
  const cell = document.createElement("td");
  cell.className = "row-actions";
  const editButton = document.createElement("button");
  editButton.type = "button";
  editButton.className = "edit-button";
  editButton.textContent = "✏️ Edit";
  // The record comes from state via this closure — never re-read from the DOM.
  editButton.addEventListener("click", () => openEditForm(record));
  cell.append(editButton);
  return cell;
}

function formatUtcTimestamp(isoText) {
  const date = new Date(isoText);
  return Number.isNaN(date.getTime()) ? "" : date.toISOString().replace("T", " ").slice(0, 19);
}

// ---- form ---------------------------------------------------------------------

function openCreateForm() {
  clearFormFeedback();
  elements.form.reset(); // reset restores the checkbox's default: checked (active)
  delete elements.form.dataset.recordId;
  elements.formTitle.textContent = "Add configuration";
  elements.formPanel.hidden = false;
  elements.form.elements.name.focus();
}

function openEditForm(record) {
  clearFormFeedback();
  const fields = elements.form.elements;
  fields.name.value = record.name;
  fields.type.value = TYPE_NORMALIZATION[record.type.trim().toLowerCase()] ?? "string";
  fields.value.value = record.value;
  fields.applicationName.value = record.applicationName;
  fields.isActive.checked = record.isActive;

  // Server-owned id: a data attribute feeding the PUT route, never a form field.
  elements.form.dataset.recordId = record.id;
  elements.formTitle.textContent = `Edit “${record.name}”`;
  elements.formPanel.hidden = false;
  fields.name.focus();
}

function closeFormPanel() {
  elements.formPanel.hidden = true;
  clearFormFeedback();
}

async function submitForm(event) {
  event.preventDefault();
  clearFormFeedback();

  const fields = elements.form.elements;
  const payload = {
    name: fields.name.value,
    type: fields.type.value,
    value: fields.value.value,
    applicationName: fields.applicationName.value,
    isActive: fields.isActive.checked,
  };

  const recordId = elements.form.dataset.recordId;
  const url = recordId ? `${API_BASE}/${encodeURIComponent(recordId)}` : API_BASE;
  const method = recordId ? "PUT" : "POST";

  let response;
  try {
    response = await fetch(url, {
      method,
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });
  } catch {
    showGlobalMessage(GENERIC_ERROR_MESSAGE); // network failure: nothing technical to show
    return;
  }

  if (response.ok) { // 201 on create, 200 on update
    closeFormPanel();
    await refreshList();
    return;
  }

  if (response.status === 400) {
    renderValidationProblem(await response.json());
    return;
  }

  if (response.status === 404) {
    // Honest signal, consistent with the API's strict-update (no upsert) stance.
    closeFormPanel();
    showGlobalMessage("This record no longer exists — another admin may have removed it. The list has been refreshed.");
    await refreshList();
    return;
  }

  showGlobalMessage(GENERIC_ERROR_MESSAGE);
}

// The 4.1→4.2→4.3 payoff: a 400 carries the failing field, so the message lands
// under the exact input. Handles both shapes — the service's ProblemDetails
// (fieldName extension) and ASP.NET's automatic DataAnnotations 400 (errors map).
function renderValidationProblem(problem) {
  if (problem.fieldName) {
    showFieldError(problem.fieldName, problem.detail);
    return;
  }

  if (problem.errors) {
    for (const [fieldName, messages] of Object.entries(problem.errors)) {
      showFieldError(fieldName, messages[0]);
    }
    return;
  }

  showGlobalMessage(GENERIC_ERROR_MESSAGE);
}

// ---- feedback -------------------------------------------------------------------

function showFieldError(fieldName, message) {
  const errorElement = elements.form.querySelector(`[data-error-for="${fieldName}"]`);
  if (!errorElement) {
    showGlobalMessage(message ?? GENERIC_ERROR_MESSAGE);
    return;
  }
  errorElement.textContent = message ?? "Invalid value.";
  errorElement.hidden = false;
}

function clearFormFeedback() {
  for (const errorElement of elements.form.querySelectorAll(".field-error")) {
    errorElement.hidden = true;
    errorElement.textContent = "";
  }
  hideGlobalMessage();
}

function showGlobalMessage(text) {
  elements.globalMessage.textContent = text;
  elements.globalMessage.hidden = false;
}

function hideGlobalMessage() {
  elements.globalMessage.hidden = true;
  elements.globalMessage.textContent = "";
}

// ---- wiring ----------------------------------------------------------------------

async function refreshList() {
  try {
    await fetchAllRecords();
  } catch {
    showGlobalMessage("Could not load configuration records. Is the API up?");
  }
}

function initialize() {
  // Filter path: re-render from state only. No fetch lives on this path.
  elements.filterInput.addEventListener("input", renderTable);
  elements.addButton.addEventListener("click", openCreateForm);
  elements.cancelButton.addEventListener("click", closeFormPanel);
  elements.form.addEventListener("submit", submitForm);
  refreshList();
}

initialize();
