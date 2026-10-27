// page.js: the page's shared parts (data, theme, selection, source panel, tabs and search).
// The views (dependency graph, class diagram) talk to it through window.vbadoc:
//   vbadoc.select(id)                 select a module (id = module name) or member (id = "Module.Name"), or null
//   vbadoc.selection()                the selected id, or null
//   vbadoc.onSelectionChange(fn)      fn(id) after the selection changes
//   vbadoc.theme() / onThemeChange(fn)  "light" or "dark"
//   vbadoc.activeView() / onViewChange(fn)  "graph" or "classes"
(function () {
  "use strict";

  var data = JSON.parse(document.getElementById("vbadoc-data").textContent);
  var modulesById = new Map();
  var membersById = new Map();
  var lateBoundByMember = new Map();
  data.modules.forEach(function (m) { modulesById.set(m.id, m); });
  data.members.forEach(function (m) { membersById.set(m.id, m); });
  data.lateBound.forEach(function (l) {
    if (!lateBoundByMember.has(l.from)) lateBoundByMember.set(l.from, []);
    lateBoundByMember.get(l.from).push(l);
  });

  var KIND_LABELS = {
    standard: "Standard module", "class": "Class", form: "UserForm", document: "Document module",
    sub: "Sub", "function": "Function", propertyGet: "Property Get", propertyLet: "Property Let",
    propertySet: "Property Set", variable: "Variable", constant: "Const", "enum": "Enum", enumMember: "Enum value",
    type: "Type", event: "Event", declare: "Declare"
  };
  var ROLE_LABELS = {
    macro: "Macro",
    worksheetFunction: "Possible worksheet function",
    builtInEventHandler: "Event handler",
    withEventsHandler: "WithEvents handler",
    controlEventHandler: "Control event handler",
    interfaceImplementation: "Implements an interface member"
  };

  function $(id) { return document.getElementById(id); }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"]/g, function (c) {
      return c === "&" ? "&amp;" : c === "<" ? "&lt;" : c === ">" ? "&gt;" : "&quot;";
    });
  }

  // localStorage can throw when the page is opened from disk, so every access is guarded.
  function loadSetting(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* not remembered */ } }

  var listeners = { selection: [], theme: [], view: [] };
  function emit(name, value) {
    listeners[name].forEach(function (fn) {
      try { fn(value); } catch (e) { console.error(e); }
    });
  }

  // ---- Theme ----

  var root = document.documentElement;
  var themeButton = $("theme-toggle");
  var systemLight = window.matchMedia ? window.matchMedia("(prefers-color-scheme: light)") : null;

  function currentTheme() { return root.getAttribute("data-theme") === "light" ? "light" : "dark"; }

  function showTheme() {
    var dark = currentTheme() === "dark";
    themeButton.setAttribute("aria-pressed", dark ? "true" : "false");
    themeButton.querySelector(".theme-label").textContent = dark ? "Dark" : "Light";
  }

  function applyTheme(theme) {
    if (theme === currentTheme()) return;
    root.setAttribute("data-theme", theme);
    showTheme();
    emit("theme", theme);
  }

  themeButton.addEventListener("click", function () {
    var next = currentTheme() === "dark" ? "light" : "dark";
    saveSetting("vbadoc-theme", next); // the user's choice now wins over the system setting
    applyTheme(next);
  });

  if (systemLight && systemLight.addEventListener) {
    systemLight.addEventListener("change", function (e) {
      var stored = loadSetting("vbadoc-theme");
      if (stored !== "light" && stored !== "dark") applyTheme(e.matches ? "light" : "dark");
    });
  }
  showTheme();

  // ---- Source text ----

  var sources = new Map();      // module id -> array of lines
  var highlighted = new Map();  // module id -> array of highlighted HTML lines (cached)
  var statusBox = $("source-status");
  var statusText = $("source-status-text");

  function splitLines(text) {
    var lines = text.split(/\r\n|\n|\r/);
    if (lines.length > 0 && lines[lines.length - 1] === "") lines.pop();
    return lines;
  }

  function setStatus(message, isWarning) {
    statusText.textContent = message;
    statusText.title = message;
    statusText.classList.toggle("warning", !!isWarning);
  }

  if (data.project.sourceMode === "embedded") {
    statusBox.classList.add("embedded");
    document.querySelectorAll("pre.vbadoc-source").forEach(function (pre) {
      sources.set(pre.getAttribute("data-module"), splitLines(pre.textContent));
    });
  } else {
    setStatus("Source not loaded");
  }

  function hex(buffer) {
    return Array.prototype.map.call(new Uint8Array(buffer), function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
  }

  // Reads the module files from the folder the user picked. Only files directly in that folder count, so a
  // .git folder or other subfolders are ignored. Files are decoded with the project's code page.
  function loadFolder(files) {
    var byName = new Map();
    Array.prototype.forEach.call(files, function (file) {
      var path = (file.webkitRelativePath || file.name).split("/");
      if (path.length <= 2) byName.set(path[path.length - 1].toLowerCase(), file);
    });

    var decoder;
    var decoderNote = "";
    try {
      decoder = new TextDecoder(data.project.encoding);
    } catch (e) {
      decoder = new TextDecoder("windows-1252");
      decoderNote = " This browser can't decode code page " + data.project.codePage + "; non-ASCII characters may be wrong.";
    }

    var missing = [];
    var changed = [];
    var found = 0;
    var reads = data.modules.map(function (module) {
      var file = byName.get(module.file.toLowerCase());
      if (!file) { missing.push(module.file); return Promise.resolve(); }
      return file.arrayBuffer().then(function (buffer) {
        found++;
        sources.set(module.id, splitLines(decoder.decode(buffer)));
        highlighted.delete(module.id);
        if (!window.crypto || !crypto.subtle) return undefined;
        return crypto.subtle.digest("SHA-256", buffer).then(function (digest) {
          if (hex(digest).slice(0, 16) !== module.hash) changed.push(module.file);
        });
      });
    });

    return Promise.all(reads).then(function () {
      if (found === 0) {
        setStatus("No source files for this project in that folder. Choose the folder that contains this page.", true);
      } else if (changed.length > 0) {
        setStatus(changed.length + " file(s) changed since this page was made, so lines may not match. Run vbadoc again. (" +
          changed.sort().join(", ") + ")" + decoderNote, true);
      } else if (missing.length > 0) {
        setStatus("Loaded " + found + " file(s); missing: " + missing.sort().join(", ") + decoderNote, true);
      } else {
        setStatus("Loaded " + found + " file(s)" + decoderNote, decoderNote !== "");
      }
      renderSource();
    });
  }

  var folderInput = $("load-folder");
  folderInput.addEventListener("change", function () {
    if (folderInput.files && folderInput.files.length > 0) loadFolder(folderInput.files);
  });

  function highlightedLines(moduleId) {
    if (!highlighted.has(moduleId)) highlighted.set(moduleId, window.VbaHighlight.highlightLines(sources.get(moduleId)));
    return highlighted.get(moduleId);
  }

  // ---- Selection and the source panel ----

  var selection = null;
  var showWholeModule = false;
  var panelHeader = $("source-header");
  var panelBody = $("source-body");

  function select(id) {
    id = id || null;
    if (id !== null && !modulesById.has(id) && !membersById.has(id)) return false;
    if (id === selection) return true;
    selection = id;
    showWholeModule = false;
    renderSource();
    emit("selection", selection);
    return true;
  }

  function moduleKindLabel(module) {
    return module.isInterface ? "Interface" : KIND_LABELS[module.kind] || module.kind;
  }

  function renderHeader(module, member) {
    var html;
    if (member) {
      html = "<h2>" + escapeHtml(member.name) + "<span class=\"kind\">" + escapeHtml(KIND_LABELS[member.kind] || member.kind) +
        " in <a href=\"#\" data-select=\"" + escapeHtml(module.id) + "\">" + escapeHtml(module.id) + "</a></span></h2>" +
        "<p class=\"signature\">" + escapeHtml(member.signature) + "</p>";
      var badges = [];
      if (member.role !== "none") {
        var role = ROLE_LABELS[member.role] || member.role;
        if (member.role === "controlEventHandler" && module.kind === "document") role += " (the control is on the sheet, not in the VBA project)";
        badges.push("<span class=\"badge\">" + escapeHtml(role) + "</span>");
      }
      if (member.unreferenced) {
        badges.push("<span class=\"badge warning\" title=\"Nothing in the code refers to it. It may still be used by Application.Run, a button or late-bound code.\">Unreferenced</span>");
      }
      if (badges.length) html += "<div class=\"badges\">" + badges.join("") + "</div>";

      var late = lateBoundByMember.get(member.id) || [];
      if (late.length) {
        html += "<div class=\"muted\">Late-bound, so not resolved:</div><ul class=\"late-bound\">" + late.map(function (l) {
          return "<li><code>" + escapeHtml(l.expression) + "</code> (line " + l.line + ")</li>";
        }).join("") + "</ul>";
      }
      if (sources.has(module.id)) {
        html += "<div class=\"actions\"><button type=\"button\" class=\"button\" id=\"toggle-context\">" +
          (showWholeModule ? "Show this member only" : "Show in the whole module") + "</button></div>";
      }
    } else {
      html = "<h2>" + escapeHtml(module.id) + "<span class=\"kind\">" + escapeHtml(moduleKindLabel(module)) + "</span></h2>" +
        "<p class=\"signature\">" + escapeHtml(module.file) + " · " + module.lines + " lines</p>";
    }
    panelHeader.innerHTML = html;

    var toggle = $("toggle-context");
    if (toggle) toggle.addEventListener("click", function () { showWholeModule = !showWholeModule; renderSource(); });
    panelHeader.querySelectorAll("[data-select]").forEach(function (link) {
      link.addEventListener("click", function (e) { e.preventDefault(); select(link.getAttribute("data-select")); });
    });
  }

  function renderSource() {
    if (selection === null) {
      panelHeader.innerHTML = "";
      panelBody.innerHTML = "<p class=\"source-message\">Select a module or member to see its source.</p>";
      return;
    }

    var member = membersById.get(selection) || null;
    var module = modulesById.get(member ? member.module : selection);
    renderHeader(module, member);

    if (!sources.has(module.id)) {
      if (data.project.sourceMode === "folder") {
        panelBody.innerHTML = "<div class=\"source-message\"><p>The source isn't loaded yet. Choose the folder that " +
          "contains this page and its .bas, .cls and .frm files.</p><button type=\"button\" class=\"button primary\" id=\"load-folder-prompt\">Load folder</button></div>";
        $("load-folder-prompt").addEventListener("click", function () { folderInput.click(); });
      } else {
        panelBody.innerHTML = "<p class=\"source-message\">No source was found for " + escapeHtml(module.id) + ".</p>";
      }
      return;
    }

    var lines = highlightedLines(module.id);
    var first = 1;
    var last = lines.length;
    if (member && !showWholeModule) {
      first = Math.max(1, member.start);
      last = Math.min(lines.length, member.end);
    }

    var html = [];
    for (var n = first; n <= last; n++) {
      var inMember = member && showWholeModule && n >= member.start && n <= member.end;
      html.push("<div class=\"line" + (inMember ? " in-member" : "") + "\" data-line=\"" + n + "\"><span class=\"ln\">" + n +
        "</span><span class=\"lt\">" + (lines[n - 1] || " ") + "</span></div>");
    }
    panelBody.innerHTML = "<div class=\"code\" role=\"region\" aria-label=\"Source code\">" + html.join("") + "</div>";

    var firstInMember = panelBody.querySelector(".in-member");
    if (firstInMember) firstInMember.scrollIntoView({ block: "start" });
    else panelBody.scrollTop = 0;
  }

  // ---- Showing, hiding and resizing the source panel ----
  //
  // One panel serves both views, so both share its size and whether it's shown. The size is the panel's share of
  // the layout (of its width, or of its height when the page is narrow and the panel sits below the view), kept as
  // the --source-size CSS variable. Until the user resizes it, the stylesheet's defaults apply. Neither view needs
  // telling: the graph's SVG fills its box and keeps its zoom, and the class diagram's box scrolls.

  var layout = $("layout");
  var sourcePanel = $("source-panel");
  var splitter = $("source-splitter");
  var sourceToggle = $("source-toggle");
  var stacked = window.matchMedia ? window.matchMedia("(max-width: 900px)") : { matches: false };
  var MIN_SOURCE_SIZE = 0.2;
  var MAX_SOURCE_SIZE = 0.7;
  var KEY_STEP = 0.02;

  function clampSize(size) { return Math.min(MAX_SOURCE_SIZE, Math.max(MIN_SOURCE_SIZE, size)); }

  function currentSourceSize() {
    var rect = layout.getBoundingClientRect();
    var panel = sourcePanel.getBoundingClientRect();
    return stacked.matches ? panel.height / rect.height : panel.width / rect.width;
  }

  // size: a share of the layout, or null for the stylesheet's default.
  function setSourceSize(size, remember) {
    if (size === null) {
      layout.style.removeProperty("--source-size");
    } else {
      size = clampSize(size);
      layout.style.setProperty("--source-size", (size * 100).toFixed(1) + "%");
    }
    if (remember) saveSetting("vbadoc-source-size", size === null ? "" : size.toFixed(3));
    updateSplitter();
  }

  function updateSplitter() {
    splitter.setAttribute("aria-orientation", stacked.matches ? "horizontal" : "vertical");
    if (!sourcePanel.hidden) splitter.setAttribute("aria-valuenow", String(Math.round(currentSourceSize() * 100)));
  }

  function setSourceVisible(visible, remember) {
    sourcePanel.hidden = !visible;
    splitter.hidden = !visible;
    layout.classList.toggle("source-hidden", !visible);
    sourceToggle.setAttribute("aria-pressed", visible ? "true" : "false");
    if (remember) saveSetting("vbadoc-source-hidden", visible ? "false" : "true");
    updateSplitter();
  }

  sourceToggle.addEventListener("click", function () { setSourceVisible(sourcePanel.hidden, true); });
  $("source-close").addEventListener("click", function () {
    setSourceVisible(false, true);
    sourceToggle.focus(); // the button that had focus has gone
  });

  // Dragging. The panel is on the right (or at the bottom), so its size is the distance from the pointer to the
  // layout's far edge.
  function sizeAtPointer(event) {
    var rect = layout.getBoundingClientRect();
    return stacked.matches ? (rect.bottom - event.clientY) / rect.height : (rect.right - event.clientX) / rect.width;
  }

  splitter.addEventListener("pointerdown", function (event) {
    if (event.button !== 0) return;
    splitter.setPointerCapture(event.pointerId);
    splitter.classList.add("dragging");
    document.body.classList.add("resizing");
    event.preventDefault();
  });
  splitter.addEventListener("pointermove", function (event) {
    if (splitter.hasPointerCapture(event.pointerId)) setSourceSize(sizeAtPointer(event), false);
  });
  function endDrag(event) {
    if (!splitter.hasPointerCapture(event.pointerId)) return;
    splitter.releasePointerCapture(event.pointerId);
    splitter.classList.remove("dragging");
    document.body.classList.remove("resizing");
    setSourceSize(sizeAtPointer(event), true);
  }
  splitter.addEventListener("pointerup", endDrag);
  splitter.addEventListener("pointercancel", endDrag);
  splitter.addEventListener("dblclick", function () { setSourceSize(null, true); });

  // Keyboard, as for a separator: the arrows move the splitter, Home and End make the panel smallest or largest.
  splitter.addEventListener("keydown", function (event) {
    var grow = stacked.matches ? { ArrowUp: 1, ArrowDown: -1 } : { ArrowLeft: 1, ArrowRight: -1 };
    var size;
    if (grow[event.key]) size = currentSourceSize() + grow[event.key] * KEY_STEP;
    else if (event.key === "Home") size = MIN_SOURCE_SIZE;
    else if (event.key === "End") size = MAX_SOURCE_SIZE;
    else if (event.key === "Enter") size = null; // like double-click: back to the default
    else return;
    setSourceSize(size, true);
    event.preventDefault();
  });

  if (stacked.addEventListener) stacked.addEventListener("change", updateSplitter);

  var storedSize = parseFloat(loadSetting("vbadoc-source-size"));
  setSourceSize(isNaN(storedSize) ? null : storedSize, false);
  setSourceVisible(loadSetting("vbadoc-source-hidden") !== "true", false);

  // ---- Tabs ----

  var tabs = [$("tab-graph"), $("tab-classes")];
  var activeView = "graph";

  function showView(name, focus) {
    activeView = name;
    tabs.forEach(function (tab) {
      var on = tab.id === "tab-" + name;
      tab.setAttribute("aria-selected", on ? "true" : "false");
      tab.tabIndex = on ? 0 : -1;
      $(tab.getAttribute("aria-controls")).hidden = !on;
      if (on && focus) tab.focus();
    });
    saveSetting("vbadoc-view", name);
    emit("view", name);
  }

  tabs.forEach(function (tab, index) {
    tab.addEventListener("click", function () { showView(tab.id.slice(4)); });
    tab.addEventListener("keydown", function (e) {
      if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
      var next = tabs[(index + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
      showView(next.id.slice(4), true);
      e.preventDefault();
    });
  });

  // ---- Search ----

  var searchBox = $("search");
  var idsByLowerCase = new Map();
  var options = [];
  data.modules.forEach(function (m) {
    idsByLowerCase.set(m.id.toLowerCase(), m.id);
    options.push("<option value=\"" + escapeHtml(m.id) + "\">" + escapeHtml(moduleKindLabel(m)) + "</option>");
  });
  data.members.forEach(function (m) {
    idsByLowerCase.set(m.id.toLowerCase(), m.id);
    options.push("<option value=\"" + escapeHtml(m.id) + "\">" + escapeHtml(m.signature) + "</option>");
  });
  $("search-list").innerHTML = options.join("");

  function searchFor(text, allowPartial) {
    var wanted = text.trim().toLowerCase();
    if (!wanted) return;
    var id = idsByLowerCase.get(wanted);
    if (!id && allowPartial) {
      var keys = Array.from(idsByLowerCase.keys());
      var match = keys.find(function (k) { return k.indexOf(wanted) === 0; }) ||
                  keys.find(function (k) { return k.indexOf(wanted) >= 0; });
      id = match ? idsByLowerCase.get(match) : null;
    }
    if (id) select(id);
  }

  searchBox.addEventListener("input", function () { searchFor(searchBox.value, false); }); // picking from the list
  searchBox.addEventListener("keydown", function (e) { if (e.key === "Enter") searchFor(searchBox.value, true); });

  // ---- Drop-down menus ----

  // The toolbars' drop-downs (<details class="dropdown">) close when you press anywhere outside them, including
  // starting to pan or drag a view, or press Escape. Opening one therefore closes any other.
  var dropDowns = document.querySelectorAll("details.dropdown");
  document.addEventListener("pointerdown", function (event) {
    dropDowns.forEach(function (d) { if (d.open && !d.contains(event.target)) d.open = false; });
  }, true);
  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape") dropDowns.forEach(function (d) { d.open = false; });
  });

  // ---- Start ----

  window.vbadoc = {
    data: data,
    modulesById: modulesById,
    membersById: membersById,
    select: select,
    selection: function () { return selection; },
    onSelectionChange: function (fn) { listeners.selection.push(fn); },
    theme: currentTheme,
    onThemeChange: function (fn) { listeners.theme.push(fn); },
    activeView: function () { return activeView; },
    onViewChange: function (fn) { listeners.view.push(fn); }
  };

  var storedView = loadSetting("vbadoc-view");
  showView(storedView === "classes" ? "classes" : "graph");
  renderSource();
})();
