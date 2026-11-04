// page.js: vbadoc's part of the page: its data, the selection, what the source panel shows, Home and the trail, Back
// and Forward, and search. The frame around it (theme, tabs, side panel, Load folder, source text) is the shell's
// (shell.js in the shared library). The views (dependency graph, class diagram) talk to it through window.vbadoc:
//   vbadoc.select(id)                 select a module (id = module name) or member (id = "Module.Name"), or null
//   vbadoc.selection()                the selected id, or null
//   vbadoc.onSelectionChange(fn)      fn(id) after the selection changes
//   vbadoc.theme() / onThemeChange(fn)  "light" or "dark"
//   vbadoc.activeView() / onViewChange(fn)  "graph" or "classes"
//   vbadoc.home() / setHome(id) / onHomeChange(fn)  the Home module or member (where the trail starts), or null
(function () {
  "use strict";

  var shell = window.ExcelDocumenterShell.create({ tool: "vbadoc", panel: "source", rememberView: true });
  var data = shell.data;
  var modulesById = new Map();
  var membersById = new Map();
  var lateBoundByMember = new Map();
  data.modules.forEach(function (m) { modulesById.set(m.id, m); });
  data.members.forEach(function (m) { membersById.set(m.id, m); });
  data.lateBound.forEach(function (l) {
    if (!lateBoundByMember.has(l.from)) lateBoundByMember.set(l.from, []);
    lateBoundByMember.get(l.from).push(l);
  });

  // Each node's edges, for the source panel's Dependencies list. Module ids are also member-level node ids
  // (references to the module itself), so the two levels are indexed separately.
  function indexEdges(edges) {
    var index = { uses: new Map(), usedBy: new Map() };
    function add(map, id, edge) {
      if (!map.has(id)) map.set(id, []);
      map.get(id).push(edge);
    }
    edges.forEach(function (e) {
      add(index.uses, e.from, e);
      add(index.usedBy, e.to, e);
    });
    return index;
  }
  var moduleEdges = indexEdges(data.moduleEdges);
  var memberEdges = indexEdges(data.memberEdges);

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

  var escapeHtml = shell.escapeHtml;
  var settings = shell.settings;

  var listeners = { selection: [], home: [] };
  function emit(name, value) {
    listeners[name].forEach(function (fn) {
      try { fn(value); } catch (e) { console.error(e); }
    });
  }

  // ---- Source text ----
  //
  // Embedded in the page, or read with Load folder and decoded with the project's code page (see shell.js).

  var decoder;
  var decoderNote = "";
  try {
    decoder = new TextDecoder(data.project.encoding);
  } catch (e) {
    decoder = new TextDecoder("windows-1252");
    decoderNote = " This browser can't decode code page " + data.project.codePage + "; non-ASCII characters may be wrong.";
  }

  shell.source.init({
    mode: data.project.sourceMode,
    attribute: "data-module",
    files: function () { return data.modules.map(function (m) { return { id: m.id, file: m.file, hash: m.hash }; }); },
    decode: function (buffer) { return decoder.decode(buffer); },
    note: decoderNote,
    highlight: function (lines) { return window.VbaHighlight.highlightLines(lines); },
    noFiles: "No source files for this project in that folder. Choose the folder that contains this page.",
    mismatch: "lines may not match",
    loaded: function () { renderSource(); }
  });

  // ---- Selection and the source panel ----

  var selection = null;
  var showWholeModule = false;
  var panelHeader = shell.panel.header;
  var panelBody = shell.panel.body;

  function select(id) {
    id = id || null;
    if (id !== null && !modulesById.has(id) && !membersById.has(id)) return false;
    if (id === selection) return true;
    selection = id;
    showWholeModule = false;
    if (id !== null) addStep(id);
    renderSource();
    renderTrail();
    shell.explorer.reveal(selection);
    rememberInHistory(id);
    emit("selection", selection);
    return true;
  }

  // ---- Home and the trail ----
  //
  // Home is one module or member per workbook, remembered per browser. The trail is the route walked from Home
  // (or, without one, from the first selection): every selection adds a step, whichever view or list it came
  // from. Selecting something already on the trail cuts the trail back to it, so loops collapse and going back
  // is just clicking an earlier step. Clearing the selection keeps the trail. It lives only as long as the page.

  var homeKey = "home|" + data.project.title; // stored settings are shared by every page opened from disk
  var home = settings.load(homeKey);
  if (home !== null && !modulesById.has(home) && !membersById.has(home)) home = null; // the workbook has changed
  var trail = home ? [home] : [];
  var trailExpanded = false;
  var TRAIL_LIMIT = 7; // longer trails show the first step, "…" and the last TRAIL_LIMIT - 2

  function addStep(id) {
    var at = trail.indexOf(id);
    if (at >= 0) trail = trail.slice(0, at + 1);
    else trail.push(id);
  }

  // Back to Home (if any) plus where you are now.
  function restartTrail() {
    trail = home ? [home] : [];
    if (selection !== null && selection !== home) trail.push(selection);
    trailExpanded = false;
  }

  function setHome(id) {
    id = id || null;
    if (id !== null && !modulesById.has(id) && !membersById.has(id)) return false;
    if (id === home) return true;
    home = id;
    if (home) settings.save(homeKey, home); else settings.remove(homeKey);
    if (home) restartTrail(); // the trail is the route from Home, so a new Home starts a new trail
    renderSource();
    renderTrail();
    emit("home", home);
    return true;
  }

  function stepText(id) {
    var member = membersById.get(id);
    return member ? member.module + "." + member.name : id;
  }

  function renderTrail() {
    var box = $("trail");
    // A lone step that is just the selection says nothing new; Home is always worth showing.
    box.hidden = !(home || trail.length > 1);
    if (box.hidden) { $("trail-steps").innerHTML = ""; return; }
    var shown = trail.map(function (id, index) { return { id: id, index: index }; });
    if (!trailExpanded && trail.length > TRAIL_LIMIT) {
      shown = [shown[0], null].concat(shown.slice(trail.length - (TRAIL_LIMIT - 2)));
    }
    $("trail-steps").innerHTML = shown.map(function (step) {
      if (step === null) {
        return "<li><button type=\"button\" class=\"trail-more\" id=\"trail-more\" title=\"Show all " + trail.length +
          " steps\">…</button></li>";
      }
      var isHome = step.index === 0 && step.id === home;
      var current = step.id === selection;
      return "<li><button type=\"button\" class=\"trail-step" + (isHome ? " home" : "") + "\" data-step=\"" + step.index + "\"" +
        (current ? " aria-current=\"step\"" : "") + " title=\"" + escapeHtml(step.id) + (isHome ? " (Home)" : "") + "\">" +
        (isHome ? "<span aria-hidden=\"true\">★ </span>" : "") + escapeHtml(stepText(step.id)) + "</button></li>";
    }).join("");
    $("trail-clear").hidden = trail.length <= (home ? 1 : 0);
  }

  $("trail").addEventListener("click", function (event) {
    if (event.target.closest("#trail-more")) { trailExpanded = true; renderTrail(); return; }
    if (event.target.closest("#trail-clear")) { restartTrail(); renderTrail(); return; }
    var step = event.target.closest("[data-step]");
    if (step) select(trail[Number(step.getAttribute("data-step"))]);
  });

  // ---- Back and Forward ----
  //
  // Each selection is added to the browser's history as the page address's #fragment (e.g. Sales-vba.html#Order.Save),
  // so Back and Forward step through selections, and an address with a fragment opens with that selection.

  var followingHistory = false;

  function idFromAddress() {
    var fragment = location.hash.slice(1);
    if (!fragment) return null;
    try { return decodeURIComponent(fragment); } catch (e) { return null; }
  }

  function rememberInHistory(id) {
    if (followingHistory || idFromAddress() === id) return;
    try {
      history.pushState(null, "", "#" + (id === null ? "" : encodeURIComponent(id)));
    } catch (e) { /* some browsers refuse history changes for pages opened from disk; selection still works */ }
  }

  function followAddress() {
    followingHistory = true;
    try { select(idFromAddress()); } finally { followingHistory = false; }
  }

  window.addEventListener("popstate", followAddress);

  function moduleKindLabel(module) {
    return module.isInterface ? "Interface" : KIND_LABELS[module.kind] || module.kind;
  }

  // ---- Dependencies list ----
  //
  // What the selection uses and what uses it: direct links only, at the selection's own level (a module lists
  // module edges, a member lists member edges), whatever the graph's filters. Each entry selects its target,
  // so the list is also a way to walk the graph. Collapsed by default; whether it's open is remembered.

  var dependenciesOpen = settings.load("dependencies-open") === "true";

  var EDGE_TAGS = {
    implements: { uses: "implements", usedBy: "implemented by" },
    events: { uses: "events", usedBy: "events" }
  };
  var EDGE_TITLES = {
    implements: { uses: "Implements this interface or interface member", usedBy: "Implements the selection" },
    events: { uses: "Raises or handles this event, or holds a WithEvents variable of this class", usedBy: "Raises or handles the selection's events" }
  };

  // How an entry reads: a member is shown by name, with its module when that differs from the selection's.
  function nodeText(id, ownModule) {
    var member = membersById.get(id);
    if (!member) {
      var module = modulesById.get(id);
      return { name: id, detail: ownModule !== null ? "the module itself" : moduleKindLabel(module) };
    }
    return {
      name: member.module === ownModule ? member.name : member.module + "." + member.name,
      detail: KIND_LABELS[member.kind] || member.kind
    };
  }

  function dependencyList(edges, direction, otherEnd, ownModule) {
    if (edges.length === 0) return "<p class=\"muted none\">Nothing</p>";
    var entries = edges.map(function (e) { return { edge: e, id: otherEnd(e), text: nodeText(otherEnd(e), ownModule) }; });
    // Case-insensitive, without localeCompare, so the order is the same in every browser language.
    function key(entry) { return entry.text.name.toLowerCase() + " | " + entry.id; }
    entries.sort(function (a, b) { return key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0; });
    return "<ul>" + entries.map(function (entry) {
      var e = entry.edge;
      var tag = EDGE_TAGS[e.kind]
        ? " <span class=\"edge-tag " + e.kind + "\" title=\"" + EDGE_TITLES[e.kind][direction] + "\">" + EDGE_TAGS[e.kind][direction] + "</span>"
        : "";
      var count = e.count > 1 ? " <span class=\"muted\" title=\"Number of references\">×" + e.count + "</span>" : "";
      return "<li><a href=\"#\" data-select=\"" + escapeHtml(entry.id) + "\">" + escapeHtml(entry.text.name) + "</a>" +
        " <span class=\"muted\">" + escapeHtml(entry.text.detail) + "</span>" + tag + count + "</li>";
    }).join("") + "</ul>";
  }

  function dependenciesHtml(id, index, ownModule) {
    // Recursion is a self-loop: noted, not listed (selecting the selection does nothing).
    var recursive = false;
    function notSelf(e) { if (e.from === e.to) { recursive = true; return false; } return true; }
    var uses = (index.uses.get(id) || []).filter(notSelf);
    var usedBy = (index.usedBy.get(id) || []).filter(notSelf);
    return "<details class=\"dependencies\" id=\"source-dependencies\"" + (dependenciesOpen ? " open" : "") + ">" +
      "<summary>Dependencies <span class=\"muted\">uses " + uses.length + " · used by " + usedBy.length + "</span></summary>" +
      (recursive ? "<p class=\"muted recursive\">Calls itself (recursion).</p>" : "") +
      "<h3>Uses (" + uses.length + ")</h3>" +
      "<div class=\"dependency-list\" data-direction=\"uses\">" + dependencyList(uses, "uses", function (e) { return e.to; }, ownModule) + "</div>" +
      "<h3>Used by (" + usedBy.length + ")</h3>" +
      "<div class=\"dependency-list\" data-direction=\"usedBy\">" + dependencyList(usedBy, "usedBy", function (e) { return e.from; }, ownModule) + "</div>" +
      "</details>";
  }

  function homeButton(id) {
    var isHome = id === home;
    return "<button type=\"button\" class=\"home-toggle\" id=\"home-toggle\" aria-pressed=\"" + isHome + "\" title=\"" +
      (isHome ? "This is Home. Click to clear Home." : "Make this Home, where the trail starts") + "\" aria-label=\"Home\">" +
      (isHome ? "★" : "☆") + "</button>";
  }

  function renderHeader(module, member) {
    var html;
    if (member) {
      html = "<h2>" + escapeHtml(member.name) + homeButton(member.id) + "<span class=\"kind\">" + escapeHtml(KIND_LABELS[member.kind] || member.kind) +
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
      if (shell.source.has(module.id)) {
        html += "<div class=\"actions\"><button type=\"button\" class=\"button\" id=\"toggle-context\">" +
          (showWholeModule ? "Show this member only" : "Show in the whole module") + "</button></div>";
      }
    } else {
      html = "<h2>" + escapeHtml(module.id) + homeButton(module.id) + "<span class=\"kind\">" + escapeHtml(moduleKindLabel(module)) + "</span></h2>" +
        "<p class=\"signature\">" + escapeHtml(module.file) + " · " + module.lines + " lines</p>";
    }
    html += dependenciesHtml(member ? member.id : module.id, member ? memberEdges : moduleEdges, member ? module.id : null);
    panelHeader.innerHTML = html;

    $("home-toggle").addEventListener("click", function () { setHome(home === selection ? null : selection); });
    var toggle = $("toggle-context");
    if (toggle) toggle.addEventListener("click", function () { showWholeModule = !showWholeModule; renderSource(); });
    // Noted on click, not on the "toggle" event, which comes later: by then a new selection may have redrawn the header.
    var dependencies = $("source-dependencies");
    dependencies.querySelector("summary").addEventListener("click", function () {
      dependenciesOpen = !dependencies.open;
      settings.save("dependencies-open", dependenciesOpen ? "true" : "false");
    });
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

    if (!shell.source.has(module.id)) {
      if (data.project.sourceMode === "folder") {
        // The shell opens Load folder from the button.
        panelBody.innerHTML = "<div class=\"source-message\"><p>The source isn't loaded yet. Choose the folder that " +
          "contains this page and its .bas, .cls and .frm files.</p><button type=\"button\" class=\"button primary\" id=\"load-folder-prompt\">Load folder</button></div>";
      } else {
        panelBody.innerHTML = "<p class=\"source-message\">No source was found for " + escapeHtml(module.id) + ".</p>";
      }
      return;
    }

    // A member shows its own lines, or the whole module with its lines marked.
    var count = shell.source.lines(module.id).length;
    var first = 1;
    var last = count;
    if (member && !showWholeModule) {
      first = Math.max(1, member.start);
      last = Math.min(count, member.end);
    }
    var marked = member && showWholeModule ? { first: member.start, last: member.end } : null;
    panelBody.innerHTML = shell.source.codeHtml(module.id, first, last, marked);

    var firstInMember = panelBody.querySelector(".marked");
    if (firstInMember) firstInMember.scrollIntoView({ block: "start" });
    else panelBody.scrollTop = 0;
  }

  // ---- Explorer ----
  //
  // Two layouts: modules in the folders their '@Folder("Parent.Child") comments give (the Rubberduck convention), and
  // modules by kind. Folders come first when any module has one; without any, there's only the kinds. Each module lists
  // its members in source order, with an Enum's values under it.

  var KIND_GROUPS = [
    ["standard", "Standard modules"], ["class", "Classes"], ["interface", "Interfaces"], ["form", "Forms"],
    ["document", "Document modules"]
  ];

  function byLowerName(a, b) {
    var x = a.toLowerCase();
    var y = b.toLowerCase();
    return x < y ? -1 : x > y ? 1 : a < b ? -1 : a > b ? 1 : 0;
  }

  function memberNode(member, children) {
    return {
      id: member.id,
      label: member.name,
      detail: KIND_LABELS[member.kind] || member.kind,
      badge: member.role !== "none" ? ROLE_LABELS[member.role] || member.role : null,
      className: member.unreferenced ? "unreferenced" : "",
      title: member.signature + (member.unreferenced ? " (nothing in the code refers to it)" : ""),
      children: children
    };
  }

  function moduleNode(module, showKind, membersOf) {
    var members = (membersOf.get(module.id) || []).slice().sort(function (a, b) { return a.start - b.start || a.declaration - b.declaration; });
    var valuesOf = new Map();
    members.forEach(function (m) {
      if (m.kind !== "enumMember" || !m.parent) return;
      if (!valuesOf.has(m.parent)) valuesOf.set(m.parent, []);
      valuesOf.get(m.parent).push(m);
    });
    var children = members
      .filter(function (m) { return !(m.kind === "enumMember" && m.parent && members.some(function (e) { return e.kind === "enum" && e.name === m.parent; })); })
      .map(function (m) { return memberNode(m, m.kind === "enum" ? (valuesOf.get(m.name) || []).map(function (v) { return memberNode(v); }) : undefined); });
    return { id: module.id, label: module.id, detail: showKind ? moduleKindLabel(module) : "", title: module.file, children: children };
  }

  function explorerLayouts() {
    var membersOf = new Map();
    data.members.forEach(function (m) {
      if (!membersOf.has(m.module)) membersOf.set(m.module, []);
      membersOf.get(m.module).push(m);
    });
    var modules = data.modules.slice().sort(function (a, b) { return byLowerName(a.id, b.id); });

    var types = {
      name: "types", label: "Types", title: "Modules by kind",
      nodes: KIND_GROUPS.map(function (group) {
        var inGroup = modules.filter(function (m) { return (m.isInterface ? "interface" : m.kind) === group[0]; });
        return { label: group[1], detail: String(inGroup.length), children: inGroup.map(function (m) { return moduleNode(m, false, membersOf); }) };
      }).filter(function (group) { return group.children.length > 0; })
    };
    if (!modules.some(function (m) { return m.folder; })) return [types];
    return [{ name: "folders", label: "Folders", title: "Modules in their '@Folder folders", nodes: folderTree(modules, membersOf) }, types];
  }

  // Folders nest at the dots. A module with no folder goes in a "No folder" group at the end.
  function folderTree(modules, membersOf) {
    var root = { folders: new Map(), modules: [] };
    var loose = [];
    modules.forEach(function (m) {
      var path = (m.folder || "").split(".").map(function (part) { return part.trim(); }).filter(function (part) { return part !== ""; });
      if (path.length === 0) { loose.push(m); return; }
      var at = root;
      path.forEach(function (part) {
        if (!at.folders.has(part)) at.folders.set(part, { folders: new Map(), modules: [] });
        at = at.folders.get(part);
      });
      at.modules.push(m);
    });

    function count(folder) {
      var total = folder.modules.length;
      folder.folders.forEach(function (child) { total += count(child); });
      return total;
    }

    function folderNodes(folder) {
      return Array.from(folder.folders.keys()).sort(byLowerName).map(function (name) {
        var child = folder.folders.get(name);
        return {
          label: name,
          folder: true,
          detail: String(count(child)),
          children: folderNodes(child).concat(child.modules.map(function (m) { return moduleNode(m, true, membersOf); }))
        };
      });
    }

    var nodes = folderNodes(root);
    if (loose.length > 0) {
      nodes.push({ label: "No folder", detail: String(loose.length), children: loose.map(function (m) { return moduleNode(m, true, membersOf); }) });
    }
    return nodes;
  }

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
    theme: shell.theme,
    onThemeChange: shell.onThemeChange,
    activeView: shell.activeView,
    onViewChange: shell.onViewChange,
    home: function () { return home; },
    setHome: setHome,
    onHomeChange: function (fn) { listeners.home.push(fn); }
  };

  shell.explorer.setLayouts(explorerLayouts(), select);
  shell.start();
  // The views read the selection when they first draw, so an address's selection is made before they do.
  if (idFromAddress() !== null) followAddress();
  renderSource();
  renderTrail();
})();
