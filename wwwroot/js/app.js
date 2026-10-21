// Asistente de triaje en el formulario. Todo el contenido dinámico se inserta con textContent
// (nunca innerHTML) para que ninguna respuesta pueda inyectar HTML.
(function () {
  "use strict";
  var btn = document.getElementById("btn-suggest");
  if (!btn) return;

  var desc = document.getElementById("description");
  var panel = document.getElementById("suggest-panel");
  var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');

  function el(tag, text, cls) {
    var node = document.createElement(tag);
    if (text !== undefined) node.textContent = text;
    if (cls) node.className = cls;
    return node;
  }

  function showError(message) {
    panel.hidden = false;
    panel.className = "suggest-panel err";
    panel.replaceChildren(el("p", message));
  }

  function showSuggestion(s) {
    panel.hidden = false;
    panel.className = "suggest-panel";

    var dl = el("dl");
    dl.appendChild(el("dt", "Categoría sugerida"));
    dl.appendChild(el("dd", s.category_label
      ? s.category_label + (s.confidence ? " (confianza " + s.confidence + ")" : "")
      : "Sin coincidencia clara: elíjala manualmente"));
    dl.appendChild(el("dt", "Prioridad sugerida"));
    dl.appendChild(el("dd", s.priority_label));
    dl.appendChild(el("dt", "Primer paso de diagnóstico"));
    dl.appendChild(el("dd", s.first_step));
    dl.appendChild(el("dt", "Por qué"));
    var ul = el("ul");
    (s.reasons || []).forEach(function (r) { ul.appendChild(el("li", r)); });
    dl.appendChild(el("dd")).appendChild(ul);

    var apply = el("button", "Aplicar categoría y prioridad", "btn btn-primary");
    apply.type = "button";
    var note = el("p", "", "muted small");
    apply.addEventListener("click", function () {
      if (s.category) document.getElementById("category").value = s.category;
      document.getElementById("priority").value = s.priority;
      note.textContent = "Aplicado. Puede corregir los campos antes de guardar.";
    });

    panel.replaceChildren(dl, apply, note);
  }

  btn.addEventListener("click", function () {
    var text = desc.value.trim();
    if (text.length < 10) {
      showError("Escriba primero la descripción (mínimo 10 caracteres).");
      desc.focus();
      return;
    }
    btn.disabled = true;
    fetch(btn.dataset.url, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-CSRF-Token": tokenInput ? tokenInput.value : ""
      },
      credentials: "same-origin",
      body: JSON.stringify({ description: text })
    })
      .then(function (res) {
        return res.json().then(function (body) { return { ok: res.ok, body: body }; });
      })
      .then(function (r) {
        if (r.ok) showSuggestion(r.body);
        else showError(r.body.error || "No se pudo obtener la sugerencia.");
      })
      .catch(function () { showError("No se pudo contactar al servidor. Intente de nuevo."); })
      .then(function () { btn.disabled = false; });
  });
})();

// Campanita de notificaciones: acciones recientes sobre incidentes (crear, editar, gestionar, resolver, cerrar, reabrir).
// Igual que arriba: todo el texto entra con textContent, nunca con innerHTML.
(function () {
  "use strict";
  var bell = document.getElementById("bell");
  if (!bell) return;

  var btn = document.getElementById("bell-btn");
  var count = document.getElementById("bell-count");
  var panel = document.getElementById("bell-panel");
  var list = document.getElementById("bell-list");
  var empty = document.getElementById("bell-empty");
  var sub = document.getElementById("bell-sub");
  var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
  var open = false;

  function el(tag, text, cls) {
    var node = document.createElement(tag);
    if (text) node.textContent = text;
    if (cls) node.className = cls;
    return node;
  }

  function setBadge(n) {
    count.hidden = n < 1;
    count.textContent = n > 9 ? "9+" : String(n);
    btn.setAttribute("aria-label", n > 0
      ? "Notificaciones: " + n + (n === 1 ? " nueva" : " nuevas")
      : "Notificaciones");
  }

  function render(feed) {
    var items = feed.items || [];
    var nodes = items.map(function (it) {
      var li = el("li", "", it.unread ? "bell-item unread" : "bell-item");
      var a = el("a");
      a.href = bell.dataset.incidents + "/" + encodeURIComponent(it.incidentId);
      var top = el("span", "", "bell-top");
      top.appendChild(el("strong", it.label));
      top.appendChild(el("span", it.code, "bell-code"));
      a.appendChild(top);
      var meta = [it.restaurant, it.actor, it.when].filter(Boolean).join(" · ");
      a.appendChild(el("span", meta, "bell-meta"));
      if (it.detail) a.appendChild(el("span", it.detail, "bell-detail"));
      li.appendChild(a);
      return li;
    });
    list.replaceChildren.apply(list, nodes);
    empty.hidden = items.length > 0;
    sub.textContent = feed.unread > 0 ? feed.unread + (feed.unread === 1 ? " nueva" : " nuevas") : "";
  }

  function fetchFeed() {
    return fetch(bell.dataset.feed, { credentials: "same-origin", headers: { "Accept": "application/json" } })
      .then(function (res) { if (!res.ok) throw new Error("http " + res.status); return res.json(); });
  }

  function refresh() {
    // Con el panel abierto no se vuelve a dibujar la lista (evita saltos mientras se lee).
    if (open) return;
    fetchFeed().then(function (feed) { setBadge(feed.unread); render(feed); }).catch(function () { /* sin red: se reintenta */ });
  }

  function markRead() {
    fetch(bell.dataset.read, {
      method: "POST",
      headers: { "X-CSRF-Token": tokenInput ? tokenInput.value : "", "Accept": "application/json" },
      credentials: "same-origin"
    }).then(function (res) { if (res.ok) setBadge(0); }).catch(function () { /* se marcará la próxima vez */ });
  }

  function setOpen(value) {
    open = value;
    panel.hidden = !value;
    btn.setAttribute("aria-expanded", value ? "true" : "false");
    if (value) {
      // Primero se muestra lo nuevo resaltado y luego se marca como leído.
      fetchFeed().then(function (feed) { render(feed); markRead(); })
        .catch(function () { sub.textContent = "No se pudo cargar."; });
    } else {
      refresh();
    }
  }

  btn.addEventListener("click", function () { setOpen(!open); });
  document.addEventListener("click", function (e) { if (open && !bell.contains(e.target)) setOpen(false); });
  document.addEventListener("keydown", function (e) {
    if (open && e.key === "Escape") { setOpen(false); btn.focus(); }
  });
  document.addEventListener("visibilitychange", function () { if (!document.hidden) refresh(); });

  refresh();
  setInterval(function () { if (!document.hidden) refresh(); }, 20000);
})();
