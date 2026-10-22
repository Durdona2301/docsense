const $ = (id) => document.getElementById(id);
const paths = {
  upload: "M12 16V3m-5 5 5-5 5 5M4 15v5h16v-5",
  file: "M14 2H5v20h14V7l-5-5Zm0 0v6h5M8 12h8m-8 4h8",
  book: "M12 5C8 2 4 2 2 3v17c4-1 7-1 10 1m0-16c4-3 8-3 10-2v17c-4-1-7-1-10 1V5Z",
  search: "m16 16 5 5M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0Z",
  send: "m22 2-7 20-4-9L2 9 22 2ZM11 13 22 2",
  arrow: "M4 12h16m-6-6 6 6-6 6",
  back: "M20 12H4m6-6-6 6 6 6",
  chevron: "m6 9 6 6 6-6",
  close: "m6 6 12 12M6 18 18 6",
  shield: "m12 2 9 4v6c0 5-5 8-9 10-4-2-9-5-9-10V6l9-4Zm-4 10 3 3 5-6",
  external: "M14 3h7v7m0-7L10 14M10 3H3v18h18v-7",
  copy: "M9 9h12v12H9V9ZM15 9V3H3v12h6",
  trash: "M3 6h18M9 6V3h6v3M5 6l1 15h12l1-15M10 10v7m4-7v7",
  refresh: "M20 7A9 9 0 1 0 21 14M20 2v6h-6",
};
function icon(name) {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.setAttribute("aria-hidden", "true");
  const path = document.createElementNS(svg.namespaceURI, "path");
  path.setAttribute("d", paths[name] || paths.file);
  svg.append(path);
  return svg;
}
document
  .querySelectorAll("[data-icon]")
  .forEach((node) => node.replaceChildren(icon(node.dataset.icon)));
function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}
let documents = [];
let aiEnabled = false;
let busy = false;
let answer = null;
let currentDocument = null;
let bootFailed = false;

async function api(path, options = {}) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 240_000);
  try {
    const response = await fetch(`/api${path}`, {
      ...options,
      signal: controller.signal,
      headers: { "X-DocSense": "1", ...options.headers },
    });
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      throw new Error(
        problem.detail ||
          (response.status === 429
            ? "The workspace is busy. Wait a moment and try again."
            : `Request failed (${response.status}). Please try again.`),
      );
    }
    return response.status === 204 ? null : await response.json();
  } catch (error) {
    if (error.name === "AbortError")
      throw new Error(
        "This request took too long. Check the workspace and Ollama, then try again.",
      );
    if (error instanceof TypeError)
      throw new Error(
        "Cannot reach the workspace. Check that DocSense is running, then retry.",
      );
    throw error;
  } finally {
    clearTimeout(timeout);
  }
}
function showError(error) {
  $("alert-text").textContent = error.message || String(error);
  $("alert").hidden = false;
}
function setBusy(value, message = "") {
  busy = value;
  $("activity").textContent = message;
  $("ask-form").setAttribute("aria-busy", String(value));
  $("ask-label").textContent = value ? "Working…" : "Ask question";
  document
    .querySelectorAll(".mutation, .suggestion, .document-name, #scope")
    .forEach((node) => {
      node.disabled = value;
    });
  updateAsk();
}
function updateAsk() {
  $("ask-button").disabled =
    busy || documents.length === 0 || $("question").value.trim().length < 3;
}
async function task(message, action) {
  if (busy) return;
  $("alert").hidden = true;
  setBusy(true, message);
  try {
    await action();
  } catch (error) {
    showError(error);
  } finally {
    setBusy(false);
  }
}
function clearAnswer() {
  answer = null;
  $("result").hidden = true;
  $("ready").hidden = documents.length === 0;
}
function bytesText(bytes) {
  return bytes < 1024
    ? `${bytes} B`
    : bytes < 1024 ** 2
      ? `${(bytes / 1024).toFixed(1)} KB`
      : `${(bytes / 1024 ** 2).toFixed(1)} MB`;
}
async function refreshLibrary() {
  documents = await api("/documents");
  const previous = $("scope").value;
  $("scope").replaceChildren(
    new Option(`All documents (${documents.length})`, ""),
  );
  for (const doc of documents) $("scope").append(new Option(doc.name, doc.id));
  if (documents.some((d) => d.id === previous)) $("scope").value = previous;
  renderLibrary();
  $("welcome").hidden = documents.length > 0;
  $("ready").hidden = documents.length === 0 || answer !== null;
  updateAsk();
}
function renderLibrary() {
  $("document-count").textContent = String(documents.length);
  $("library").replaceChildren();
  if (!documents.length)
    $("library").append(
      el("p", "muted", "Your library is empty. Add your first document above."),
    );
  for (const doc of documents) {
    const row = el(
      "div",
      `document${$("scope").value === doc.id ? " selected" : ""}`,
    );
    row.dataset.kind = doc.kind;
    const file = el("span", "file-icon");
    file.append(icon("file"));
    const info = el("div");
    const name = el("button", "document-name", doc.name);
    name.type = "button";
    name.setAttribute("aria-label", `Read ${doc.name}`);
    name.disabled = busy;
    name.addEventListener("click", () => openDocument(doc));
    info.append(
      name,
      el(
        "p",
        "document-meta",
        `${doc.kind} · ${bytesText(doc.bytes)} · ${doc.chunkCount} ${doc.chunkCount === 1 ? "passage" : "passages"}`,
      ),
    );
    const remove = el("button", "icon-button mutation");
    remove.setAttribute("aria-label", `Remove ${doc.name}`);
    remove.append(icon("trash"));
    remove.disabled = busy;
    remove.addEventListener("click", () => {
      row.querySelector(".delete-confirm")?.remove();
      const confirm = el("div", "delete-confirm", "Remove this document?");
      const yes = el("button", "mutation", "Remove");
      const no = el("button", "", "Keep");
      yes.addEventListener("click", () =>
        task("Removing document…", async () => {
          await api(`/documents/${doc.id}`, { method: "DELETE" });
          if (currentDocument?.id === doc.id) closeReader();
          clearAnswer();
          await refreshLibrary();
        }),
      );
      no.addEventListener("click", () => {
        confirm.remove();
        remove.focus();
      });
      confirm.append(yes, no);
      row.append(confirm);
      no.focus();
    });
    row.append(file, info, remove);
    $("library").append(row);
  }
}
function markText(text, question) {
  const fragment = document.createDocumentFragment();
  const terms = [
    ...new Set(question.toLowerCase().match(/[\p{L}\p{N}]{3,}/gu) || []),
  ].filter(
    (t) =>
      ![
        "the",
        "how",
        "what",
        "are",
        "does",
        "this",
        "from",
        "with",
        "for",
        "long",
      ].includes(t),
  );
  if (!terms.length) {
    fragment.append(text);
    return fragment;
  }
  const regex = new RegExp(`(${terms.join("|")})`, "giu");
  let cursor = 0;
  for (const match of text.matchAll(regex)) {
    fragment.append(text.slice(cursor, match.index), el("mark", "", match[0]));
    cursor = match.index + match[0].length;
  }
  fragment.append(text.slice(cursor));
  return fragment;
}
function focusSource(number) {
  document
    .querySelectorAll(".source")
    .forEach((s) => s.classList.remove("focused"));
  const source = $(`source-${number}`);
  if (!source) return;
  source.classList.add("focused");
  source.querySelector("details").open = true;
  source.focus({ preventScroll: true });
  source.scrollIntoView({
    behavior: matchMedia("(prefers-reduced-motion: reduce)").matches
      ? "instant"
      : "smooth",
    block: "nearest",
  });
}
function renderAnswer(result) {
  answer = result;
  $("result").hidden = false;
  $("ready").hidden = true;
  $("answer-heading").textContent =
    result.mode === "extractive"
      ? "Relevant passage"
      : result.mode === "no-evidence"
        ? "More evidence needed"
        : "Answer";
  $("asked-question").textContent = result.question;
  $("answer-text").textContent = result.text;
  $("answer-notice").textContent =
    result.notice ||
    (result.mode === "generated"
      ? "Generated by a local model. Check the source passages."
      : "");
  $("sources-section").hidden = result.sources.length === 0;
  $("citations").replaceChildren();
  $("sources").replaceChildren();
  for (const source of result.sources) {
    const citation = el("button", "citation", `[${source.number}]`);
    citation.type = "button";
    citation.setAttribute(
      "aria-label",
      `Inspect source ${source.number}: ${source.documentName}`,
    );
    citation.addEventListener("click", () => focusSource(source.number));
    $("citations").append(citation);
    const card = el("article", "source");
    card.id = `source-${source.number}`;
    card.tabIndex = -1;
    const head = el("div", "source-head");
    head.append(icon("file"));
    const title = el("div");
    title.append(
      el("h3", "", `${source.number}. ${source.documentName}`),
      el(
        "p",
        "source-meta",
        `${source.page ? `Page ${source.page} · ` : ""}Passage ${source.ordinal}`,
      ),
    );
    head.append(title);
    const details = el("details");
    const summary = el("summary", "", "Show full passage");
    const quote = el("blockquote", "passage");
    quote.append(markText(source.content, result.question));
    details.append(summary, quote);
    details.addEventListener("toggle", () => {
      summary.textContent = details.open
        ? "Collapse passage"
        : "Show full passage";
    });
    const read = el("button", "text-button", "Read document");
    read.append(icon("arrow"));
    read.addEventListener("click", () => {
      const doc = documents.find((d) => d.id === source.documentId);
      if (doc) openDocument(doc, source.chunkId);
    });
    card.append(head, quote, details, read);
    // One passage per card. Details controls the full-length passage, preview stays visible while collapsed.
    const preview = quote.cloneNode(true);
    preview.classList.add("preview");
    card.insertBefore(preview, details);
    details.append(quote);
    details.addEventListener("toggle", () => {
      preview.hidden = details.open;
    });
    preview.classList.add("passage-preview");
    $("sources").append(card);
  }
}
async function ask(event) {
  event?.preventDefault();
  if ($("ask-button").disabled) return;
  await task(
    aiEnabled
      ? "Searching your documents and checking sources…"
      : "Finding relevant passages…",
    async () => {
      const scope = $("scope").value;
      const result = await api("/ask", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          question: $("question").value.trim(),
          documentIds: scope ? [scope] : [],
        }),
      });
      renderAnswer(result);
    },
  );
}
async function upload(file) {
  if (!file) return;
  if (!/\.(pdf|md|txt)$/i.test(file.name)) {
    showError(new Error("Choose a PDF, Markdown (.md), or TXT file."));
    return;
  }
  if (file.size === 0 || file.size > 8 * 1024 * 1024) {
    showError(new Error("Choose a non-empty file up to 8 MB."));
    return;
  }
  await task("Reading and indexing your document…", async () => {
    const form = new FormData();
    form.append("file", file);
    const result = await api("/documents", { method: "POST", body: form });
    clearAnswer();
    closeReader();
    await refreshLibrary();
    $("question").focus();
    if (result.notice) showError(new Error(result.notice));
  });
  $("file-input").value = "";
}
async function samples() {
  await task("Adding the fictional sample library…", async () => {
    const results = await api("/samples", { method: "POST" });
    clearAnswer();
    closeReader();
    await refreshLibrary();
    $("scope").value = "";
    $("question").value = "How long is the trial period?";
    $("question").focus();
    const warning = results.find((r) => r.notice && !r.duplicate)?.notice;
    if (warning) showError(new Error(warning));
  });
}
async function openDocument(doc, chunkId) {
  await task("Opening document…", async () => {
    const chunks = await api(`/documents/${doc.id}/chunks`);
    currentDocument = doc;
    $("workspace").hidden = true;
    $("reader").hidden = false;
    $("reader-title").textContent = doc.name;
    $("reader-meta").textContent =
      `${doc.kind} · ${chunks.length} passages · ${doc.embeddedChunks} embedded`;
    $("reindex-button").hidden = !aiEnabled;
    $("reader-content").replaceChildren();
    for (const chunk of chunks) {
      const section = el("section", "reader-chunk");
      section.id = `chunk-${chunk.id}`;
      section.append(
        el(
          "h2",
          "",
          `${chunk.page ? `Page ${chunk.page} · ` : ""}Passage ${chunk.ordinal}`,
        ),
        el("p", "", chunk.content),
      );
      $("reader-content").append(section);
    }
    if (chunkId) $(`chunk-${chunkId}`)?.scrollIntoView({ block: "start" });
    else $("close-reader").focus();
  });
}
function closeReader() {
  currentDocument = null;
  $("reader").hidden = true;
  $("workspace").hidden = false;
}
async function boot() {
  await task("Connecting to your local workspace…", async () => {
    const status = await api("/status");
    aiEnabled = status.aiEnabled;
    $("mode-label").textContent = status.mode;
    $("connection").textContent = aiEnabled
      ? "Local model enabled"
      : "No AI model connected";
    await refreshLibrary();
    bootFailed = false;
    $("load-samples").replaceChildren(
      icon("book"),
      document.createTextNode("Load sample library"),
    );
  });
  if (documents.length === 0 && $("alert").hidden === false) {
    bootFailed = true;
    $("mode-label").textContent = "Workspace unavailable";
    $("connection").textContent = "Check the server and database";
    $("library").replaceChildren(
      el("p", "muted", "Your library could not be loaded. Retry below."),
    );
    $("load-samples").replaceChildren(
      icon("refresh"),
      document.createTextNode("Retry connection"),
    );
  }
}
$("ask-form").addEventListener("submit", ask);
$("question").addEventListener("input", updateAsk);
$("question").addEventListener("keydown", (event) => {
  if ((event.ctrlKey || event.metaKey) && event.key === "Enter") {
    event.preventDefault();
    ask();
  }
});
$("scope").addEventListener("change", () => {
  clearAnswer();
  renderLibrary();
});
$("upload-button").addEventListener("click", () => $("file-input").click());
$("file-input").addEventListener("change", () =>
  upload($("file-input").files[0]),
);
$("load-samples").addEventListener("click", () =>
  bootFailed ? boot() : samples(),
);
$("try-sample").addEventListener("click", samples);
$("dismiss-alert").addEventListener("click", () => {
  $("alert").hidden = true;
});
$("close-reader").addEventListener("click", () => {
  closeReader();
  $("question").focus();
});
$("reindex-button").addEventListener("click", () =>
  task("Creating document embeddings…", async () => {
    await api(`/documents/${currentDocument.id}/reindex`, { method: "POST" });
    await refreshLibrary();
    currentDocument = documents.find((d) => d.id === currentDocument.id);
    $("reader-meta").textContent =
      `${currentDocument.kind} · ${currentDocument.chunkCount} passages · ${currentDocument.embeddedChunks} embedded`;
  }),
);
$("copy-answer").addEventListener("click", async () => {
  if (!answer) return;
  try {
    const sources = answer.sources
      .map(
        (s) =>
          `[${s.number}] ${s.documentName}, passage ${s.ordinal}${s.page ? `, page ${s.page}` : ""}`,
      )
      .join("\n");
    await navigator.clipboard.writeText(`${answer.text}\n\n${sources}`);
    $("activity").textContent = "Answer and source references copied.";
  } catch {
    showError(
      new Error("Clipboard is unavailable. Select the answer text to copy it."),
    );
  }
});
document.querySelectorAll(".suggestion").forEach((button) =>
  button.addEventListener("click", () => {
    $("question").value = button.dataset.question;
    updateAsk();
    $("question").focus();
  }),
);
let dragDepth = 0;
document.addEventListener("dragenter", (event) => {
  if (!event.dataTransfer?.types.includes("Files")) return;
  event.preventDefault();
  dragDepth++;
  if (!busy) $("drop-overlay").hidden = false;
});
document.addEventListener("dragover", (event) => {
  if (event.dataTransfer?.types.includes("Files")) event.preventDefault();
});
document.addEventListener("dragleave", (event) => {
  event.preventDefault();
  if (--dragDepth <= 0) $("drop-overlay").hidden = true;
});
document.addEventListener("drop", (event) => {
  event.preventDefault();
  dragDepth = 0;
  $("drop-overlay").hidden = true;
  if (busy) return;
  if (event.dataTransfer.files.length !== 1) {
    showError(new Error("Drop one document at a time."));
    return;
  }
  upload(event.dataTransfer.files[0]);
});
boot();
