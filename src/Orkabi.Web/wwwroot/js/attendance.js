/* ============================================================
   Orkabi — Attendance (signature optimistic tap/submit) §2
   Progressive enhancement: the page renders + is readable without
   this file. With JS, each row becomes a two-half tap target:
     - tap the LEADING half (inline-start = right in RTL) → נעדר (absent)
     - tap the TRAILING half (inline-end = left in RTL)  → נוכח (present)
   Re-tapping the SAME half toggles back to unmarked (undo).
   Submit POSTs ONE batch to /api/attendance with a session
   idempotency key; 200/409/fail handled; marks stay in DOM on fail.
   Motion is gated by prefers-reduced-motion in CSS (transforms only).
   ============================================================ */
(function () {
  "use strict";

  var stage = document.getElementById("attendance");
  if (!stage) return;

  var rows = Array.prototype.slice.call(stage.querySelectorAll(".att-row"));
  var submitBtn = stage.querySelector("[data-att-submit]");
  var errorEl = stage.querySelector("[data-att-error]");
  var elPresent = stage.querySelector("[data-count-present]");
  var elAbsent = stage.querySelector("[data-count-absent]");
  var elLeft = stage.querySelector("[data-count-left]");

  if (rows.length === 0 || !submitBtn) return;

  var shiftInstanceId = parseInt(stage.getAttribute("data-shift"), 10);
  var hasModel = stage.getAttribute("data-has-model") === "true";
  // ONE batch idempotency key per session (reused across retries on failure).
  var idempotencyKey = stage.getAttribute("data-idempotency-key") ||
    ("att-" + shiftInstanceId + "-" + Date.now());

  var isRtl = (document.documentElement.getAttribute("dir") || "").toLowerCase() === "rtl";

  // clientId -> "Present" | "Absent" | undefined
  var marks = Object.create(null);

  function setState(row, state) {
    var id = row.getAttribute("data-client-id");
    row.classList.remove("is-present", "is-absent");
    if (state === "Present") {
      row.classList.add("is-present");
      row.setAttribute("aria-pressed", "true");
      row.setAttribute("aria-label", "סמן נוכח: " + name(row));
      marks[id] = "Present";
    } else if (state === "Absent") {
      row.classList.add("is-absent");
      row.setAttribute("aria-pressed", "true");
      row.setAttribute("aria-label", "סמן נעדר: " + name(row));
      marks[id] = "Absent";
    } else {
      row.setAttribute("aria-pressed", "false");
      row.setAttribute("aria-label", name(row) + " — טרם סומן");
      delete marks[id];
    }
    refreshTally();
  }

  function name(row) {
    var n = row.querySelector(".att-row__name");
    return n ? n.textContent.trim() : "";
  }

  function tappedState(row, clientX) {
    var rect = row.getBoundingClientRect();
    var x = clientX - rect.left;
    // leading = inline-start. In RTL that is the RIGHT half (x > width/2).
    var leading = isRtl ? (x > rect.width / 2) : (x < rect.width / 2);
    return leading ? "Absent" : "Present";   // leading half → absent, trailing → present
  }

  function onTap(e) {
    var row = e.currentTarget;
    var want = tappedState(row, e.clientX);
    var id = row.getAttribute("data-client-id");
    // Re-tapping the same verdict toggles back to unmarked (undo).
    setState(row, marks[id] === want ? null : want);
    hideError();
  }

  function refreshTally() {
    var present = 0, absent = 0;
    rows.forEach(function (row) {
      var v = marks[row.getAttribute("data-client-id")];
      if (v === "Present") present++;
      else if (v === "Absent") absent++;
    });
    var left = rows.length - present - absent;
    if (elPresent) elPresent.textContent = String(present);
    if (elAbsent) elAbsent.textContent = String(absent);
    if (elLeft) elLeft.textContent = String(left);
  }

  function showError(msg) {
    if (!errorEl) return;
    errorEl.textContent = msg || "השמירה נכשלה — נסו שוב";
    errorEl.hidden = false;
  }
  function hideError() { if (errorEl) errorEl.hidden = true; }

  function token() {
    var meta = document.querySelector('meta[name="htmx-csrf"]');
    return meta ? meta.getAttribute("content") : "";
  }

  function buildBody() {
    var list = [];
    rows.forEach(function (row) {
      var id = row.getAttribute("data-client-id");
      if (marks[id]) list.push({ clientId: parseInt(id, 10), status: marks[id] });
    });
    return { shiftInstanceId: shiftInstanceId, marks: list, idempotencyKey: idempotencyKey };
  }

  function onSubmit() {
    if (!hasModel) return;
    var body = buildBody();
    if (body.marks.length === 0) return;

    // Confirm if some rows remain unmarked.
    var unmarked = rows.length - body.marks.length;
    if (unmarked > 0) {
      var ok = window.confirm("נותרו " + unmarked + " תלמידים ללא סימון — לשמור בכל זאת?");
      if (!ok) return;
    }

    submitBtn.classList.add("is-busy");
    submitBtn.disabled = true;
    hideError();

    fetch("/api/attendance", {
      method: "POST",
      credentials: "same-origin",
      headers: {
        "Content-Type": "application/json",
        "RequestVerificationToken": token()
      },
      body: JSON.stringify(body)
    }).then(function (resp) {
      if (resp.status === 200) return resp.json().then(function () { onSuccess("הנוכחות נשמרה ✓"); });
      if (resp.status === 409) return onSuccess("הנוכחות כבר נשמרה");  // already saved — never a dup
      return Promise.reject(new Error("HTTP " + resp.status));
    }).catch(function () {
      // Network/server fail: marks STAY in the DOM; a retry reuses the same idempotencyKey.
      submitBtn.classList.remove("is-busy");
      submitBtn.disabled = false;
      showError("השמירה נכשלה — נסו שוב");
    });
  }

  function onSuccess(msg) {
    submitBtn.classList.remove("is-busy");
    submitBtn.classList.add("is-done");
    submitBtn.textContent = msg;
    // Return to the home after a beat.
    window.setTimeout(function () {
      window.location.href = "/Dashboard/Instructor";
    }, 1200);
  }

  rows.forEach(function (row) { row.addEventListener("click", onTap); });
  submitBtn.addEventListener("click", onSubmit);
  refreshTally();
})();
