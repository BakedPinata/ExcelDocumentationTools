// page.js: pqdoc's part of the page: its data, the selection, what the details panel shows, and search. The frame
// around it (theme, tabs, side panel, Load folder, source text) is the shell's (shell.js in the shared library). The
// views (the dependency graph) talk to it through window.pqdoc:
//   pqdoc.select(id, other)          select a query (id = query name), or null; other is true for a Ctrl+click,
//                                    which does the other thing from focus on click
//   pqdoc.selection()                the selected id, or null
//   pqdoc.onSelectionChange(fn)      fn(id) after the selection changes
//   pqdoc.focused() / onFocusChange(fn)  whether the views show only the selection and what it's connected to; fn
//                                    when that changes and the selection doesn't
//   pqdoc.focusOnClick() / setFocusOnClick(on)  the page's Focus on click setting (the graph's Focus button)
//   pqdoc.favourites() / isFavourite(id) / toggleFavourite(id) / onFavouritesChange(fn)  the favourite queries (the shell's)
//   pqdoc.inFilter(id) / filterActive() / filterKey() / onFilterChange(fn)  whether the explorer's filter shows a query
//   pqdoc.hidden() / isHidden(id) / toggleHidden(id)   the queries hidden from the graph
//   pqdoc.shows(id) / shownKey() / onShownChange(fn)   whether the graph shows a query: kept by the Show… choices, in
//                                     the filter and not hidden. The key changes with any of them
//   pqdoc.locked() / onLockChange(fn) / lockSource(view, fn)   the shell's Lock view: whether it's on; fn(on) when it
//                                     changes, just before onShownChange; and a view's say in what locking ticks:
//                                     fn() gives { ids } for what it shows now, when it's the open view
//   pqdoc.focusFavourites() / setFocusFavourites(on)   whether the graph shows only the favourites and what they use
//                                     (with the focused selection's); changes come through onFocusChange
//   pqdoc.theme() / onThemeChange(fn)  "light" or "dark"
//   pqdoc.activeView() / onViewChange(fn)  "graph"
//   pqdoc.matches() / onSearchChange(fn)  the ids the search box matches (a Set), or null when it's empty
//   pqdoc.clearSearch()              empty the search box
//   pqdoc.uses(id) / usedBy(id)      the ids directly upstream or downstream of a query
//   pqdoc.upstream(id) / downstream(id)  the same, following the whole chain (a Set)
//   pqdoc.countText(n)               "1 query" or "n queries"
//   pqdoc.viewPart(name, part)       a view's part of every saved view (the shell's views.part): its save(),
//                                    apply(value) and ids(value). Opening a saved view sets each part and the
//                                    selection, then the views hear of it through onShownChange, once, and read
//                                    the selection and its focus as they draw (onSelectionChange isn't called)
(function () {
  "use strict";

  function $(id) { return document.getElementById(id); }

  var shell = window.CodeDocumenterShell.create({ tool: "pqdoc", panel: "details", rememberView: false });
  var data = shell.data;
  var queriesById = new Map();
  var uses = new Map();
  var usedBy = new Map();
  data.queries.forEach(function (q) {
    queriesById.set(q.id, q);
    uses.set(q.id, []);
    usedBy.set(q.id, []);
  });
  var edgeLines = new Map(); // "from\u0000to" -> the lines of to's source that name from
  data.edges.forEach(function (e) {
    uses.get(e.to).push(e.from);
    usedBy.get(e.from).push(e.to);
    edgeLines.set(e.from + "\u0000" + e.to, e.lines || []);
  });

  // Everything reachable from a query by following `edges`, not counting the query itself.
  function chain(start, edges) {
    var seen = new Set();
    var stack = [start];
    while (stack.length > 0) {
      edges.get(stack.pop()).forEach(function (next) {
        if (!seen.has(next) && next !== start) { seen.add(next); stack.push(next); }
      });
    }
    return seen;
  }

  function countText(count) { return count === 1 ? "1 query" : count + " queries"; }

  var escapeHtml = shell.escapeHtml;

  var listeners = { selection: [], search: [], shown: [], lock: [] };
  var lockSources = {};
  function emit(name, value) {
    listeners[name].forEach(function (fn) {
      try { fn(value); } catch (e) { console.error(e); }
    });
  }

  // ---- Source text ----
  //
  // Each query's M code, for the details panel (FR-SRC-1): embedded with --embed-source, or read with Load folder and
  // checked against the hash taken when the page was made (see shell.js).

  // .m files are UTF-8 unless they start with a UTF-16 byte order mark, as Power Query's own exports can.
  function decode(buffer) {
    var bytes = new Uint8Array(buffer, 0, Math.min(2, buffer.byteLength));
    var encoding = bytes[0] === 0xFF && bytes[1] === 0xFE ? "utf-16le" : bytes[0] === 0xFE && bytes[1] === 0xFF ? "utf-16be" : "utf-8";
    return new TextDecoder(encoding).decode(buffer);
  }

  shell.source.init({
    mode: data.sourceMode,
    attribute: "data-query",
    files: function () {
      return data.queries.filter(function (q) { return q.file; }).map(function (q) { return { id: q.id, file: q.file, hash: q.hash }; });
    },
    decode: decode,
    note: "",
    highlight: function (lines) { return window.MHighlight.highlightLines(lines); },
    noFiles: "No .m files for these queries in that folder. Choose the folder that holds this page's .m files.",
    mismatch: "they may not match the graph",
    loaded: function () { renderDetails(); }
  });

  function sourceSection(query) {
    if (data.sourceMode === "none") return "";
    var heading = "<h3>Source" + (query.file ? " <span class=\"muted file-name\">" + escapeHtml(query.file) + "</span>" : "") + "</h3>";

    if (!shell.source.has(query.id)) {
      if (data.sourceMode === "folder" && query.file) {
        return heading + "<div class=\"source-message\" id=\"details-source\"><p>The source isn't loaded yet. Choose the folder " +
          "that holds this page's .m files.</p><button type=\"button\" class=\"button primary\" id=\"load-folder-prompt\">Load folder</button></div>";
      }
      return heading + "<p class=\"source-message\" id=\"details-source\">No source was found for " + escapeHtml(query.id) + ".</p>";
    }

    return heading + "<div class=\"source\" id=\"details-source\">" + shell.source.codeHtml(query.id) + "</div>";
  }

  // ---- Selection and the details panel ----

  var selection = null;
  var panelHeader = shell.panel.header;
  var panelBody = shell.panel.body;
  shell.panel.element.setAttribute("aria-live", "polite"); // the details are short, so read them out

  function select(id, other) {
    id = id || null;
    if (id !== null && !queriesById.has(id)) return false;
    // First, so the panel is open when its details are drawn. The same selection may change its focus.
    if (!shell.selectionChanged(id, other)) return true;
    selection = id;
    renderDetails();
    emit("selection", selection);
    return true;
  }

  var ROLE_TEXT = {
    root: "Root query: it uses no other query.",
    leaf: "Leaf query: no other query uses it.",
    intermediate: "Intermediate query.",
    isolated: "Isolated query: not connected to any other query."
  };

  // Case-insensitive, without localeCompare, so the order is the same in every browser language.
  function byName(a, b) {
    var x = a.toLowerCase();
    var y = b.toLowerCase();
    return x < y ? -1 : x > y ? 1 : 0;
  }

  // What a query uses or what uses it, each with the lines that name the query used: in the selection's source for Uses,
  // in the other query's for Used by.
  function queryList(selected, ids, direction) {
    return shell.references.listHtml(ids.slice().sort(byName).map(function (id) {
      var holder = direction === "uses" ? selected : id;
      var lines = edgeLines.get(direction === "uses" ? id + "\u0000" + selected : selected + "\u0000" + id) || [];
      return {
        id: id, name: id, hidden: shell.hidden.has(id),
        refs: lines.map(function (line) { return { "in": holder, source: holder, line: line, inName: holder }; })
      };
    }), "None");
  }

  // ---- Show… choices ----
  //
  // In the explorer, for the tree and the graph: queries and functions, and queries with no connections (the graph's).

  shell.choices.init({
    sections: [
      { title: "Queries", choices: [{ id: "kind:query", label: "Queries" }, { id: "kind:function", label: "Functions" }] },
      { title: "Other", choices: [{ id: "unconnected", label: "Queries with no connections" }] }
    ]
  });

  function kept(id) {
    var query = queriesById.get(id);
    return !!query && shell.choices.on(query.kind === "function" ? "kind:function" : "kind:query");
  }

  // ---- Widely used ----
  //
  // Queries used directly by at least a share of the other queries (Core counts them, as downstream), most used first.

  function widelyUsed(share) {
    var others = data.queries.length - 1;
    var least = Math.max(2, Math.ceil(share * others / 100));
    return data.queries.filter(function (q) { return q.downstream >= least; })
      .sort(function (a, b) { return b.downstream - a.downstream || byName(a.id, b.id); })
      .map(function (q) {
        return { id: q.id, label: q.id, detail: "used by " + q.downstream, title: "Used by " + q.downstream + " of the " + others + " other queries" };
      });
  }

  // ---- What the graph shows ----
  //
  // What the Show… choices keep, in the filter and not hidden. A ticked query is shown even with no connections.

  function shows(id) {
    var query = queriesById.get(id);
    if (!query || !kept(id) || shell.hidden.has(id)) return false;
    if (shell.filter.active()) return shell.filter.has(id);
    return query.role !== "isolated" || shell.choices.on("unconnected");
  }

  function shownKey() {
    return shell.filter.ids().slice().sort().join("|") + "\u0000" + shell.hidden.list().slice().sort().join("|") +
      "\u0000" + shell.choices.key();
  }

  shell.onFilterChange(function () { emit("shown"); });
  // Locking and unlocking change the filter with the lock: the graph hears of the lock, then draws once.
  shell.onLockChange(function (on) {
    emit("lock", on);
    emit("shown");
  });
  shell.onChoicesChange(function () { emit("shown"); });
  shell.onHiddenChange(function () {
    if (selection !== null) renderDetails();
    emit("shown");
  });
  // A saved view sets the selection, the filter, hiding and the Show… choices at once: the graph draws once, reading
  // the selection and its focus. The shell has taken the selection already.
  shell.onViewApplied(function (id) {
    selection = id;
    renderDetails();
    emit("shown");
  });

  // How many of the queries the selection uses or is used by are hidden.
  function hiddenNear(id) {
    var near = new Set(uses.get(id).concat(usedBy.get(id)));
    near.delete(id);
    var count = 0;
    near.forEach(function (other) { if (shell.hidden.has(other)) count++; });
    return count;
  }

  function renderDetails() {
    if (selection === null) {
      panelHeader.innerHTML = "";
      panelBody.innerHTML = "<p class=\"details-message\">Select a query to see what it uses and what uses it.</p>";
      return;
    }

    var query = queriesById.get(selection);
    var upstream = chain(selection, uses).size;
    var downstream = chain(selection, usedBy).size;
    panelHeader.innerHTML = "<h2>" + escapeHtml(query.id) + shell.favourites.buttonHtml(query.id) + shell.hidden.buttonHtml(query.id) +
      "</h2><p class=\"muted\" id=\"details-role\">" + ROLE_TEXT[query.role] +
      (query.inCycle ? " Part of a circular dependency." : "") + "</p>" +
      // What the query says about itself (FR-HTML-24): its description in the workbook, or the comments at its top.
      (query.description ? "<p class=\"description\" id=\"details-description\">" + escapeHtml(query.description) + "</p>" : "") +
      shell.hidden.noteHtml(query.id, hiddenNear(query.id));
    panelBody.innerHTML =
      "<h3><span class=\"swatch upstream\" aria-hidden=\"true\"></span>Uses (" + uses.get(selection).length + ")</h3>" +
      "<div id=\"details-uses\">" + queryList(selection, uses.get(selection), "uses") + "</div>" +
      "<h3><span class=\"swatch downstream\" aria-hidden=\"true\"></span>Used by (" + usedBy.get(selection).length + ")</h3>" +
      "<div id=\"details-used-by\">" + queryList(selection, usedBy.get(selection), "usedBy") + "</div>" +
      "<p class=\"muted reach\" id=\"details-reach\">Including indirect links, " + countText(upstream) + " feed" + (upstream === 1 ? "s" : "") +
      " this query and " + countText(downstream) + " depend" + (downstream === 1 ? "s" : "") + " on it.</p>" +
      "<div class=\"actions\"><button type=\"button\" class=\"button\" id=\"details-clear\">Clear selection</button></div>" +
      stepsHtml(query) +
      sourceSection(query);
  }

  // A query's let steps in order (FR-HTML-20), each linking to its line and listing the queries it uses, then the in
  // expression. A query without a let of its own has none: its Uses list says what it uses.
  function stepsHtml(query) {
    var steps = query.steps || [];
    if (steps.length === 0) return "";
    var named = steps.filter(function (s) { return s.name !== null; }).length;
    return "<h3>Steps (" + named + ")</h3><ol class=\"query-steps\" id=\"details-steps\">" + steps.map(function (step) {
      var label = step.name === null ? "in" : step.name;
      return "<li" + (step.name === null ? " class=\"step-in\"" : "") + "><button type=\"button\" class=\"ref-line\" data-ref-select=\"" +
        escapeHtml(query.id) + "\" data-ref-line=\"" + step.line + "\" title=\"Show line " + step.line + "\"><span class=\"ref-ln\">" + step.line +
        "</span><code>" + escapeHtml(label) + "</code></button>" +
        (step.uses.length === 0 ? "" : "<span class=\"step-uses\"><span class=\"muted\">uses</span> " + step.uses.map(function (id) {
          return "<a href=\"#\" data-select=\"" + escapeHtml(id) + "\">" + escapeHtml(id) + "</a>" +
            (shell.hidden.has(id) ? " <span class=\"hidden-tag\" title=\"Hidden from the graph\">hidden</span>" : "");
        }).join(", ") + "</span>") + "</li>";
    }).join("") + "</ol>";
  }

  panelBody.addEventListener("click", function (event) {
    if (event.target.closest("#details-clear")) { select(null); return; }
    var link = event.target.closest("[data-select]");
    if (!link) return;
    event.preventDefault();
    select(link.getAttribute("data-select"), event.ctrlKey || event.metaKey);
  });

  // ---- Explorer ----
  //
  // Two layouts: queries in their Excel query groups, nested and in Excel's order, with the rest under "Other queries",
  // as Excel's Queries pane shows them; and queries and functions apart. Groups come first when the workbook has any;
  // without any (or for a folder of .m files) there are only queries and functions.

  function queryNode(query, showKind) {
    return { id: query.id, label: query.id, detail: showKind && query.kind === "function" ? "Function" : "" };
  }

  function groupNode(label, queries, showKind) {
    return { label: label, detail: String(queries.length), children: queries.map(function (q) { return queryNode(q, showKind); }) };
  }

  function explorerLayouts() {
    var queries = data.queries.slice().sort(function (a, b) { return byName(a.id, b.id); });
    var types = {
      name: "types", label: "Types", title: "Queries and functions apart",
      nodes: [
        groupNode("Queries", queries.filter(function (q) { return q.kind !== "function"; }), false),
        groupNode("Functions", queries.filter(function (q) { return q.kind === "function"; }), false)
      ].filter(function (node) { return node.children.length > 0; })
    };
    if (data.groups.length === 0) return [types];
    return [{ name: "groups", label: "Groups", title: "Queries in their Excel query groups", nodes: groupTree(queries) }, types];
  }

  // data.groups lists each group before the groups inside it, so one pass puts every group under its parent. A group
  // shows the groups inside it, then its queries, and counts every query under it.
  function groupTree(queries) {
    var groups = data.groups.map(function (g) {
      return { label: g.name, title: g.description || "", folder: true, groups: [], queries: [] };
    });
    var top = [];
    data.groups.forEach(function (g, i) { (g.parent === null ? top : groups[g.parent].groups).push(groups[i]); });
    var loose = [];
    queries.forEach(function (q) { (q.group === null || q.group === undefined ? loose : groups[q.group].queries).push(q); });

    function finish(group) {
      var inside = group.groups.map(finish);
      var count = inside.reduce(function (total, g) { return total + Number(g.detail); }, group.queries.length);
      return {
        label: group.label, title: group.title, folder: true, detail: String(count),
        children: inside.concat(group.queries.map(function (q) { return queryNode(q, true); }))
      };
    }

    var nodes = top.map(finish);
    if (loose.length > 0) nodes.push(groupNode("Other queries", loose, true));
    return nodes;
  }

  // ---- Search ----
  //
  // Typing lights up every query whose name contains the text, and Enter selects the first of them by name.

  var searchBox = $("search");
  var searchStatus = $("search-status");
  var matches = null;

  function runSearch() {
    var wanted = searchBox.value.trim().toLowerCase();
    matches = null;
    if (wanted) {
      matches = new Set(data.queries.filter(function (q) { return q.id.toLowerCase().indexOf(wanted) >= 0; }).map(function (q) { return q.id; }));
    }
    searchStatus.hidden = matches === null;
    searchStatus.textContent = matches === null ? ""
      : matches.size === 0 ? "No query matches"
      : matches.size === 1 ? "1 match" : matches.size + " matches";
    emit("search", matches);
  }

  function clearSearch() {
    searchBox.value = "";
    runSearch();
  }

  searchBox.addEventListener("input", runSearch);
  searchBox.addEventListener("keydown", function (event) {
    if (event.key === "Enter" && matches && matches.size > 0) select(Array.from(matches).sort(byName)[0]);
    if (event.key === "Escape") clearSearch();
  });

  // / jumps to the search box from anywhere. (Escape clears the selection, in the shell.)
  document.addEventListener("keydown", function (event) {
    if (document.activeElement === searchBox) return;
    if (event.key === "/") {
      event.preventDefault();
      searchBox.focus();
    }
  });

  // ---- Start ----

  window.pqdoc = {
    data: data,
    queriesById: queriesById,
    select: select,
    selection: function () { return selection; },
    onSelectionChange: function (fn) { listeners.selection.push(fn); },
    theme: shell.theme,
    onThemeChange: shell.onThemeChange,
    activeView: shell.activeView,
    onViewChange: shell.onViewChange,
    panelFollowing: shell.panel.following,
    favourites: shell.favourites.list,
    isFavourite: shell.favourites.has,
    toggleFavourite: shell.favourites.toggle,
    inFilter: function (id) { return !shell.filter.active() || shell.filter.has(id); },
    filterActive: shell.filter.active,
    filterKey: function () { return shell.filter.ids().slice().sort().join("|"); },
    onFilterChange: shell.onFilterChange,
    hidden: shell.hidden.list,
    isHidden: shell.hidden.has,
    toggleHidden: shell.hidden.toggle,
    shows: shows,
    shownKey: shownKey,
    onShownChange: function (fn) { listeners.shown.push(fn); },
    locked: shell.lock.on,
    onLockChange: function (fn) { listeners.lock.push(fn); },
    lockSource: function (view, fn) { lockSources[view] = fn; },
    focusFavourites: shell.focusFavourites,
    setFocusFavourites: shell.setFocusFavourites,
    onFavouritesChange: shell.onFavouritesChange,
    focused: shell.focused,
    onFocusChange: shell.onFocusChange,
    focusOnClick: shell.focusOnClick,
    setFocusOnClick: shell.setFocusOnClick,
    matches: function () { return matches; },
    onSearchChange: function (fn) { listeners.search.push(fn); },
    clearSearch: clearSearch,
    uses: function (id) { return uses.get(id); },
    usedBy: function (id) { return usedBy.get(id); },
    upstream: function (id) { return chain(id, uses); },
    downstream: function (id) { return chain(id, usedBy); },
    countText: countText,
    viewPart: shell.views.part
  };

  shell.items.init({
    workbook: data.title,
    kept: kept,
    widelyUsed: widelyUsed,
    exists: function (id) { return queriesById.has(id); },
    // What Lock view ticks: what the open view shows, if it's one that can say.
    shown: function () {
      var source = lockSources[shell.activeView()];
      return source ? source() : null;
    },
    node: function (id) { return queryNode(queriesById.get(id), true); },
    describeFilter: function (ids) { return ids.length + " of " + countText(data.queries.length); }
  });
  shell.explorer.setLayouts(explorerLayouts(), select);

  // Every query can be taken, and Core has worked out what each relies on.
  shell.exports.init({
    what: "queries",
    whyNot: function () { return null; },
    uses: function (id) { return uses.get(id); },
    needs: function (id) { return queriesById.get(id).needs; },
    offers: function () { return []; },
    pickFor: function (id) { return id; },
    references: [],
    writeTo: "--new Shared.xlsx"
  });
  shell.start();
  renderDetails();
})();
