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
//   vbadoc.hidden() / isHidden(id) / toggleHidden(id)   the items hidden from the views (a hidden module takes its
//                                     members with it)
//   vbadoc.shows(id) / shownKey() / onShownChange(fn)   whether the views show a module or member: in the filter and
//                                     not hidden. The key changes with either
//   vbadoc.memberCategories            [{ id, label, kinds }]: the kinds of member the Show… menu chooses between
//   vbadoc.kept(id)                    whether the Show… choices keep a module or member (its module's kind, its own
//                                     kind, private members, references); the views apply the rest themselves
//   vbadoc.choice(id) / choiceValue(name) / choicesKey() / choicesCount() / onChoicesChange(fn)   the explorer's Show…
//                                     choices: "line:uses", "line:implements", "line:events" and "line:holds" for
//                                     lines, and "unconnected" for items with no connections
//   vbadoc.focusFavourites() / setFocusFavourites(on)   whether the views show only the favourites and what they use
//                                     (with the focused selection's); changes come through onFocusChange
//   vbadoc.viewPart(name, part)       a view's part of every saved view (the shell's views.part): its save(),
//                                     apply(value) and ids(value). Opening a saved view sets each part, then the
//                                     views hear of it through onShownChange, once
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

  var listeners = { selection: [], shown: [] };
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

  // ---- Show… choices ----
  //
  // One set, in the explorer, for the tree and every view. A choice applies wherever it means something: member kinds,
  // private members and references to the tree, the graph at member level and the class diagram's boxes; Events lines
  // to the graph and Holds in a variable to the class diagram.

  var MODULE_KINDS = [
    ["standard", "Standard modules"], ["class", "Classes"], ["interface", "Interfaces"], ["form", "UserForms"],
    ["document", "Document modules"]
  ];

  var MEMBER_CATEGORIES = [
    { id: "procedure", label: "Procedures", kinds: ["sub", "function"] },
    { id: "property", label: "Properties", kinds: ["propertyGet", "propertyLet", "propertySet"] },
    { id: "data", label: "Variables and constants", kinds: ["variable", "constant"] },
    { id: "type", label: "Enums and types", kinds: ["enum", "type"] },
    { id: "enumValue", label: "Enum values", kinds: ["enumMember"] },
    { id: "event", label: "Events", kinds: ["event"] },
    { id: "declare", label: "Declares", kinds: ["declare"] }
  ];
  var categoryOfKind = {};
  MEMBER_CATEGORIES.forEach(function (c) { c.kinds.forEach(function (k) { categoryOfKind[k] = c.id; }); });

  shell.choices.init({
    sections: [
      { title: "Modules", choices: MODULE_KINDS.map(function (k) { return { id: "module:" + k[0], label: k[1] }; }) },
      {
        title: "Members",
        choices: MEMBER_CATEGORIES.map(function (c) { return { id: "member:" + c.id, label: c.label }; })
          .concat([{ id: "private", label: "Private members" }])
      },
      {
        title: "References", radio: "references",
        choices: [{ value: "all", label: "All members" }, { value: "unreferenced", label: "Unreferenced only" }, { value: "referenced", label: "Referenced only" }]
      },
      {
        title: "Lines",
        choices: [
          { id: "line:uses", label: "Uses" }, { id: "line:implements", label: "Implements" },
          { id: "line:events", label: "Events (graph)" }, { id: "line:holds", label: "Holds in a variable (class diagram)" }
        ]
      },
      { title: "Other", choices: [{ id: "unconnected", label: "Items with no connections" }] }
    ]
  });

  function moduleKindOf(module) { return module.isInterface ? "interface" : module.kind; }

  function kept(id) {
    var member = membersById.get(id);
    var module = modulesById.get(member ? member.module : id);
    if (!module || !shell.choices.on("module:" + moduleKindOf(module))) return false;
    if (!member) return true;
    if (!shell.choices.on("member:" + categoryOfKind[member.kind])) return false;
    if (member.visibility === "private" && !shell.choices.on("private")) return false;
    var references = shell.choices.value("references");
    return references === "all" || (references === "unreferenced") === !!member.unreferenced;
  }

  // ---- What the views show ----
  //
  // What's in the filter and not hidden. A hidden module takes its members with it; a hidden member goes alone.

  var hiddenNow = new Set();

  function isHidden(id) {
    if (hiddenNow.has(id)) return true;
    var member = membersById.get(id);
    return !!member && hiddenNow.has(member.module);
  }

  function shows(id) { return inFilter(id) && !isHidden(id); }

  function shownKey() {
    return shell.filter.ids().slice().sort().join("|") + "\u0000" + shell.hidden.list().slice().sort().join("|");
  }

  shell.onFilterChange(function () { emit("shown"); });
  shell.onHiddenChange(function () {
    hiddenNow = new Set(shell.hidden.list());
    if (selection !== null) renderHeader(modulesById.get(membersById.has(selection) ? membersById.get(selection).module : selection), membersById.get(selection) || null);
    emit("shown");
  });
  // A saved view sets the selection, the filter, hiding and the Show… choices at once: the views draw once, each
  // reading the selection and its focus as it does. The shell has taken the selection already.
  shell.onViewApplied(function (id) {
    refreshFilter();
    hiddenNow = new Set(shell.hidden.list());
    selection = id;
    renderSource();
    rememberInHistory(id);
    emit("shown");
  });

  // How many of the items the selection is linked to, either way, are hidden.
  function hiddenNear(id, index) {
    var near = new Set();
    (index.uses.get(id) || []).forEach(function (e) { near.add(e.to); });
    (index.usedBy.get(id) || []).forEach(function (e) { near.add(e.from); });
    near.delete(id);
    var count = 0;
    near.forEach(function (other) { if (isHidden(other)) count++; });
    return count;
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
    return module.isInterface ? "Interface" : module.predeclared ? "Predeclared class" : KIND_LABELS[module.kind] || module.kind;
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

  // The module a node is in: a member's, or a module node's own.
  function moduleOfId(id) { var member = membersById.get(id); return member ? member.module : id; }

  // Member edges by the modules at their ends, for a module's references.
  var memberEdgesByModules = new Map();
  data.memberEdges.forEach(function (e) {
    var key = moduleOfId(e.from) + "\u0000" + moduleOfId(e.to);
    if (!memberEdgesByModules.has(key)) memberEdgesByModules.set(key, []);
    memberEdgesByModules.get(key).push(e);
  });

  // Where member edges' references are: each line once, in order, in the member that holds it.
  function referencesOf(edges) {
    var seen = new Set();
    var refs = [];
    edges.forEach(function (e) {
      (e.lines || []).forEach(function (line) {
        var key = e.from + "\u0000" + line;
        if (seen.has(key)) return;
        seen.add(key);
        var member = membersById.get(e.from);
        refs.push({ "in": e.from, source: moduleOfId(e.from), line: line, inName: member ? member.module + "." + member.name : e.from });
      });
    });
    return refs.sort(function (a, b) { return a.line - b.line; });
  }

  // One entry per item at the other end, whatever the kinds of link: a tag for each kind other than plain use, how many
  // references when more than one, and the lines they're on. At module level the lines are its members' references.
  function dependencyList(id, edges, direction, otherEnd, ownModule) {
    var byOther = new Map();
    edges.forEach(function (e) {
      var other = otherEnd(e);
      if (!byOther.has(other)) byOther.set(other, []);
      byOther.get(other).push(e);
    });
    var atModuleLevel = ownModule === null;
    var entries = Array.from(byOther.keys()).map(function (other) {
      var group = byOther.get(other);
      var text = nodeText(other, ownModule);
      var tags = [];
      var count = group.reduce(function (sum, e) { return sum + (e.kind === "uses" ? e.count : 0); }, 0);
      if (count > 1) tags.push({ label: "×" + count, title: "Number of references", className: "muted" });
      group.forEach(function (e) {
        if (EDGE_TAGS[e.kind]) tags.push({ label: EDGE_TAGS[e.kind][direction], title: EDGE_TITLES[e.kind][direction], className: "edge-tag " + e.kind });
      });
      var references = atModuleLevel
        ? memberEdgesByModules.get(direction === "uses" ? id + "\u0000" + other : other + "\u0000" + id) || []
        : group;
      return { id: other, name: text.name, detail: text.detail, tags: tags, hidden: isHidden(other), refs: referencesOf(references) };
    });
    // Case-insensitive, without localeCompare, so the order is the same in every browser language.
    function key(entry) { return entry.name.toLowerCase() + " | " + entry.id; }
    entries.sort(function (a, b) { return key(a) < key(b) ? -1 : key(a) > key(b) ? 1 : 0; });
    return { count: entries.length, html: shell.references.listHtml(entries, "Nothing") };
  }

  function dependenciesHtml(id, index, ownModule) {
    // Recursion is a self-loop: noted, not listed (selecting the selection does nothing).
    var recursive = false;
    function notSelf(e) { if (e.from === e.to) { recursive = true; return false; } return true; }
    var uses = dependencyList(id, (index.uses.get(id) || []).filter(notSelf), "uses", function (e) { return e.to; }, ownModule);
    var usedBy = dependencyList(id, (index.usedBy.get(id) || []).filter(notSelf), "usedBy", function (e) { return e.from; }, ownModule);
    return "<details class=\"dependencies\" id=\"source-dependencies\"" + (dependenciesOpen ? " open" : "") + ">" +
      "<summary>Dependencies <span class=\"muted\">uses " + uses.count + " · used by " + usedBy.count + "</span></summary>" +
      (recursive ? "<p class=\"muted recursive\">Calls itself (recursion).</p>" : "") +
      "<h3>Uses (" + uses.count + ")</h3>" +
      "<div class=\"dependency-list\" data-direction=\"uses\">" + uses.html + "</div>" +
      "<h3>Used by (" + usedBy.count + ")</h3>" +
      "<div class=\"dependency-list\" data-direction=\"usedBy\">" + usedBy.html + "</div>" +
      "</details>";
  }


  function renderHeader(module, member) {
    var html;
    if (member) {
      html = "<h2>" + escapeHtml(member.name) + shell.favourites.buttonHtml(member.id) + shell.hidden.buttonHtml(member.id) + "<span class=\"kind\">" + escapeHtml(KIND_LABELS[member.kind] || member.kind) +
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
      html = "<h2>" + escapeHtml(module.id) + shell.favourites.buttonHtml(module.id) + shell.hidden.buttonHtml(module.id) + "<span class=\"kind\">" + escapeHtml(moduleKindLabel(module)) + "</span></h2>" +
        "<p class=\"signature\">" + escapeHtml(module.file) + " · " + module.lines + " lines</p>";
    }
    var shownId = member ? member.id : module.id;
    var index = member ? memberEdges : moduleEdges;
    // A member hidden with its module says so as the module would.
    html += shell.hidden.noteHtml(member && !shell.hidden.has(member.id) && isHidden(member.id) ? module.id : shownId, hiddenNear(shownId, index));
    html += dependenciesHtml(shownId, index, member ? module.id : null);
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

  // A saved view keeps what the source panel shows of the selection: a member alone or in the whole module, and
  // whether its Dependencies are open. The shell keeps the selection.
  shell.views.part("source", {
    save: function () { return { whole: showWholeModule, dependencies: dependenciesOpen }; },
    apply: function (value) {
      value = value && typeof value === "object" ? value : {};
      showWholeModule = value.whole === true;
      if (typeof value.dependencies === "boolean") {
        dependenciesOpen = value.dependencies;
        settings.save("dependencies-open", dependenciesOpen ? "true" : "false");
      }
    }
  });

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
    return {
      id: module.id, label: module.id, title: module.file, children: children,
      detail: showKind ? moduleKindLabel(module) : module.predeclared ? "Predeclared" : ""
    };
  }

  // ---- Widely used ----
  //
  // Modules used by at least a share of the other modules (Core counts them, as usedBy), most used first, each with its
  // own members that are, under it. A member is never used by more modules than its module, so the module stands for it.

  function widelyUsed(share) {
    var others = data.modules.length - 1;
    var least = Math.max(2, Math.ceil(share * others / 100));
    function title(count) { return "Used by " + count + " of the " + others + " other modules"; }
    function byUse(a, b) { return b.usedBy - a.usedBy || byLowerName(a.id, b.id); }
    var membersOf = new Map();
    data.members.forEach(function (m) {
      if ((m.usedBy || 0) < least) return;
      if (!membersOf.has(m.module)) membersOf.set(m.module, []);
      membersOf.get(m.module).push(m);
    });
    return data.modules.filter(function (m) { return (m.usedBy || 0) >= least; }).sort(byUse).map(function (m) {
      return {
        id: m.id, label: m.id, detail: "used by " + m.usedBy, title: title(m.usedBy),
        children: (membersOf.get(m.id) || []).sort(byUse).map(function (member) {
          return { id: member.id, label: member.name, detail: "used by " + member.usedBy, title: title(member.usedBy) };
        })
      };
    });
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
    hidden: shell.hidden.list,
    isHidden: isHidden,
    toggleHidden: shell.hidden.toggle,
    shows: shows,
    shownKey: shownKey,
    onShownChange: function (fn) { listeners.shown.push(fn); },
    focusFavourites: shell.focusFavourites,
    setFocusFavourites: shell.setFocusFavourites,
    escapeHtml: shell.escapeHtml,
    roleLabel: function (role) { return ROLE_LABELS[role] || role; },
    showLine: shell.source.showLine,
    memberCategories: MEMBER_CATEGORIES,
    kept: kept,
    choice: shell.choices.on,
    choiceValue: shell.choices.value,
    choicesKey: shell.choices.key,
    choicesCount: shell.choices.count,
    onChoicesChange: shell.onChoicesChange,
    onFavouritesChange: shell.onFavouritesChange,
    viewPart: shell.views.part
  };

  // Favourites and the filter are per workbook. In the explorer's Favourites group a member shows its module, as it
  // isn't under it. The filter counts the modules it shows.
  shell.items.init({
    workbook: data.project.title,
    kept: kept,
    widelyUsed: widelyUsed,
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
  hiddenNow = new Set(shell.hidden.list());
  shell.explorer.setLayouts(explorerLayouts(), select);
  shell.start();
  // The views read the selection when they first draw, so an address's selection is made before they do.
  if (idFromAddress() !== null) followAddress();
  renderSource();
})();
