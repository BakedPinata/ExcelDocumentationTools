// page.js: vbadoc's part of the page: its data, the selection, what the source panel shows, Back and Forward, and
// search. The frame around it (theme, tabs, side panel, Load folder, source text) is the shell's
// (shell.js in the shared library). The views (dependency graph, class diagram) talk to it through window.vbadoc:
//   vbadoc.select(id, other)          select a module (id = module name) or member (id = "Module.Name"), or null;
//                                     other is true for a Ctrl+click, which does the other thing from focus on click
//   vbadoc.selection()                the selected id, or null
//   vbadoc.onSelectionChange(fn)      fn(id) after the selection changes
//   vbadoc.focused() / onFocusChange(fn)  whether the views show only the selection and what it's connected to; fn
//                                     when that changes and the selection doesn't
//   vbadoc.focusOnClick() / setFocusOnClick(on)  the page's Focus on click setting (the views' Focus buttons)
//   vbadoc.theme() / onThemeChange(fn)  "light" or "dark"
//   vbadoc.activeView() / onViewChange(fn)  "graph" or "classes"
//   vbadoc.favourites() / isFavourite(id) / toggleFavourite(id) / onFavouritesChange(fn)  the favourite modules and members (the shell's)
//   vbadoc.inFilter(id) / filterActive() / filterKey() / onFilterChange(fn)  whether the explorer's filter shows a module
//                                     or member (a ticked module brings its members, a ticked member its module)
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

  var listeners = { selection: [] };
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

  function select(id, other) {
    id = id || null;
    if (id !== null && !modulesById.has(id) && !membersById.has(id)) return false;
    // First, so the panel is open when its source is drawn and scrolled. The same selection may change its focus.
    if (!shell.selectionChanged(id, other)) return true;
    selection = id;
    showWholeModule = false;
    renderSource();
    rememberInHistory(id);
    emit("selection", selection);
    return true;
  }

  // ---- The explorer's filter ----
  //
  // While anything is ticked in the explorer, the views show only that (the shell keeps the ticks). A ticked module
  // brings all its members, and a ticked member its module: at module level the module stands for it.

  var modulesWithTickedMembers = new Set();

  function refreshFilter() {
    modulesWithTickedMembers = new Set();
    shell.filter.ids().forEach(function (id) {
      var member = membersById.get(id);
      if (member) modulesWithTickedMembers.add(member.module);
    });
  }

  // First, so the views see the new filter when they hear of it.
  shell.onFilterChange(refreshFilter);

  function inFilter(id) {
    if (!shell.filter.active() || shell.filter.has(id)) return true;
    var member = membersById.get(id);
    return member ? shell.filter.has(member.module) : modulesWithTickedMembers.has(id);
  }

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


  function renderHeader(module, member) {
    var html;
    if (member) {
      html = "<h2>" + escapeHtml(member.name) + shell.favourites.buttonHtml(member.id) + "<span class=\"kind\">" + escapeHtml(KIND_LABELS[member.kind] || member.kind) +
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
      html = "<h2>" + escapeHtml(module.id) + shell.favourites.buttonHtml(module.id) + "<span class=\"kind\">" + escapeHtml(moduleKindLabel(module)) + "</span></h2>" +
        "<p class=\"signature\">" + escapeHtml(module.file) + " · " + module.lines + " lines</p>";
    }
    html += dependenciesHtml(member ? member.id : module.id, member ? memberEdges : moduleEdges, member ? module.id : null);
    panelHeader.innerHTML = html;

    var toggle = $("toggle-context");
    if (toggle) toggle.addEventListener("click", function () { showWholeModule = !showWholeModule; renderSource(); });
    // Noted on click, not on the "toggle" event, which comes later: by then a new selection may have redrawn the header.
    var dependencies = $("source-dependencies");
    dependencies.querySelector("summary").addEventListener("click", function () {
      dependenciesOpen = !dependencies.open;
      settings.save("dependencies-open", dependenciesOpen ? "true" : "false");
    });
    panelHeader.querySelectorAll("[data-select]").forEach(function (link) {
      link.addEventListener("click", function (e) { e.preventDefault(); select(link.getAttribute("data-select"), e.ctrlKey || e.metaKey); });
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
    panelFollowing: shell.panel.following,
    focused: shell.focused,
    onFocusChange: shell.onFocusChange,
    focusOnClick: shell.focusOnClick,
    setFocusOnClick: shell.setFocusOnClick,
    favourites: shell.favourites.list,
    isFavourite: shell.favourites.has,
    toggleFavourite: shell.favourites.toggle,
    inFilter: inFilter,
    filterActive: shell.filter.active,
    filterKey: function () { return shell.filter.ids().slice().sort().join("|"); },
    onFilterChange: shell.onFilterChange,
    onFavouritesChange: shell.onFavouritesChange
  };

  // Favourites and the filter are per workbook. In the explorer's Favourites group a member shows its module, as it
  // isn't under it. The filter counts the modules it shows.
  shell.items.init({
    workbook: data.project.title,
    exists: function (id) { return modulesById.has(id) || membersById.has(id); },
    node: function (id) {
      var member = membersById.get(id);
      if (!member) return { label: id, detail: moduleKindLabel(modulesById.get(id)), title: modulesById.get(id).file };
      var node = memberNode(member);
      node.label = member.module + "." + member.name;
      return node;
    },
    describeFilter: function (ids) {
      var modules = new Set(ids.map(function (id) { var member = membersById.get(id); return member ? member.module : id; }));
      return modules.size + " of " + data.modules.length + " module" + (data.modules.length === 1 ? "" : "s");
    }
  });
  refreshFilter();
  shell.explorer.setLayouts(explorerLayouts(), select);
  shell.start();
  // The views read the selection when they first draw, so an address's selection is made before they do.
  if (idFromAddress() !== null) followAddress();
  renderSource();
})();
