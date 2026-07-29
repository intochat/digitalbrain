(function () {
  const behaviorId = window.DIGITALBRAIN_BEHAVIOR_ID || "com.digitalbrain.account-enrichment";
  const statusEl = document.getElementById("status");
  const programHost = document.getElementById("program");
  const featureHost = document.getElementById("feature");
  const saveButton = document.getElementById("save");
  const testsButton = document.getElementById("run-tests");
  const approveButton = document.getElementById("approve");
  const behaviorLabel = document.getElementById("behavior-id");

  let documentState = null;
  let programArea = null;
  let featureArea = null;

  function setStatus(text, kind) {
    statusEl.textContent = text;
    statusEl.className = kind || "";
  }

  function mountFallback(host, language) {
    const area = document.createElement("textarea");
    area.className = "monaco-editor-fallback";
    area.setAttribute("data-language", language);
    area.spellcheck = false;
    host.appendChild(area);
    return {
      getValue: () => area.value,
      setValue: (value) => {
        area.value = value || "";
      },
    };
  }

  function createEditors() {
    if (window.monaco && window.monaco.editor) {
      programArea = {
        getValue: () => programArea._editor.getValue(),
        setValue: (value) => programArea._editor.setValue(value || ""),
        _editor: window.monaco.editor.create(programHost, {
          language: "csharp",
          theme: "vs-dark",
          automaticLayout: true,
          minimap: { enabled: false },
        }),
      };
      featureArea = {
        getValue: () => featureArea._editor.getValue(),
        setValue: (value) => featureArea._editor.setValue(value || ""),
        _editor: window.monaco.editor.create(featureHost, {
          language: "plaintext",
          theme: "vs-dark",
          automaticLayout: true,
          minimap: { enabled: false },
        }),
      };
      return;
    }

    programArea = mountFallback(programHost, "csharp");
    featureArea = mountFallback(featureHost, "feature");
  }

  async function readJson(response) {
    if (!response.ok) {
      throw new Error(response.status + " " + response.statusText);
    }
    return response.json();
  }

  function applyDocument(doc) {
    documentState = doc;
    behaviorLabel.textContent = doc.behaviorId + " · " + doc.status;
    programArea.setValue(doc.programSource || "");
    featureArea.setValue(doc.featureText || "");
    const parts = [
      "status=" + doc.status,
      doc.proposedArtifactHash ? "proposed=" + doc.proposedArtifactHash.slice(0, 12) : null,
      "tests=" + (doc.testsPassed ? "passed" : "pending"),
      doc.isApproved ? "approved" : null,
      doc.lastCompileFailure ? "compile=" + doc.lastCompileFailure : null,
    ].filter(Boolean);
    setStatus(parts.join(" · "), doc.lastCompileFailure ? "fail" : doc.testsPassed ? "ok" : "");
  }

  async function load() {
    setStatus("Loading " + behaviorId + "…");
    const doc = await readJson(await fetch("/behaviors/" + encodeURIComponent(behaviorId)));
    applyDocument(doc);
  }

  async function propose() {
    setStatus("Proposing revision…");
    const body = {
      programSource: programArea.getValue(),
      featureText: featureArea.getValue(),
      featureName: (documentState && documentState.featureName) || "install",
      displayName: (documentState && documentState.displayName) || behaviorId,
      description: (documentState && documentState.description) || behaviorId,
    };
    const doc = await readJson(
      await fetch("/behaviors/" + encodeURIComponent(behaviorId) + "/propose", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      })
    );
    applyDocument(doc);
  }

  async function runTests() {
    if (!documentState || !documentState.proposedArtifactHash) {
      setStatus("Propose a revision before running tests.", "fail");
      return;
    }
    setStatus("Running BDD gate…");
    const doc = await readJson(
      await fetch("/behaviors/" + encodeURIComponent(behaviorId) + "/tests", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ artifactHash: documentState.proposedArtifactHash }),
      })
    );
    applyDocument(doc);
  }

  async function approve() {
    if (!documentState || !documentState.proposedArtifactHash) {
      setStatus("Propose a revision before approving.", "fail");
      return;
    }
    setStatus("Approving revision…");
    const approvalId =
      typeof crypto !== "undefined" && crypto.randomUUID
        ? crypto.randomUUID()
        : "00000000-0000-4000-8000-" + Date.now().toString(16).padStart(12, "0");
    const doc = await readJson(
      await fetch("/behaviors/" + encodeURIComponent(behaviorId) + "/approve", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          artifactHash: documentState.proposedArtifactHash,
          approvalId: approvalId,
        }),
      })
    );
    applyDocument(doc);
  }

  saveButton.addEventListener("click", () => propose().catch((error) => setStatus(String(error), "fail")));
  testsButton.addEventListener("click", () => runTests().catch((error) => setStatus(String(error), "fail")));
  approveButton.addEventListener("click", () => approve().catch((error) => setStatus(String(error), "fail")));

  createEditors();
  load().catch((error) => setStatus(String(error), "fail"));
})();
