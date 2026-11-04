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
//   pqdoc.theme() / onThemeChange(fn)  "light" or "dark"
//   pqdoc.activeView() / onViewChange(fn)  "graph"
//   pqdoc.matches() / onSearchChange(fn)  the ids the search box matches (a Set), or null when it's empty
//   pqdoc.clearSearch()              empty the search box
//   pqdoc.uses(id) / usedBy(id)      the ids directly upstream or downstream of a query
//   pqdoc.upstream(id) / downstream(id)  the same, following the whole chain (a Set)
//   pqdoc.countText(n)               "1 query" or "n queries"
(function () {
  "use strict";

  function $(id) { return document.getElementById(id); }

  var shell = window.ExcelDocumenterShell.create({ tool: "pqdoc", panel: "details", rememberView: false });
  var data = shell.data;
  var queriesById = new Map();
  var uses = new Map();
  var usedBy = new Map();
  data.queries.forEach(function (q) {
    queriesById.set(q.id, q);
    uses.set(q.id, []);
    usedBy.set(q.id, []);
  });
  data.edges.forEach(function (e) {
    uses.get(e.to).push(e.from);
    usedBy.get(e.from).push(e.to);
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

  var listeners = { selection: [], search: [] };
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

  function queryList(ids) {
    if (ids.length === 0) return "<p class=\"muted none\">None</p>";
    return "<ul>" + ids.slice().sort(byName).map(function (id) {
      return "<li><a href=\"#\" data-select=\"" + escapeHtml(id) + "\">" + escapeHtml(id) + "</a></li>";
    }).join("") + "</ul>";
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
    panelHeader.innerHTML = "<h2>" + escapeHtml(query.id) + shell.favourites.buttonHtml(query.id) + "</h2><p class=\"muted\" id=\"details-role\">" + ROLE_TEXT[query.role] +
      (query.inCycle ? " Part of a circular dependency." : "") + "</p>";
    panelBody.innerHTML =
      "<h3><span class=\"swatch upstream\" aria-hidden=\"true\"></span>Uses (" + uses.get(selection).length + ")</h3>" +
      "<div id=\"details-uses\">" + queryList(uses.get(selection)) + "</div>" +
      "<h3><span class=\"swatch downstream\" aria-hidden=\"true\"></span>Used by (" + usedBy.get(selection).length + ")</h3>" +
      "<div id=\"details-used-by\">" + queryList(usedBy.get(selection)) + "</div>" +
      "<p class=\"muted reach\" id=\"details-reach\">Including indirect links, " + countText(upstream) + " feed" + (upstream === 1 ? "s" : "") +
      " this query and " + countText(downstream) + " depend" + (downstream === 1 ? "s" : "") + " on it.</p>" +
      "<div class=\"actions\"><button type=\"button\" class=\"button\" id=\"details-clear\">Clear selection</button></div>" +
      sourceSection(query);
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
    countText: countText
  };

  shell.favourites.init({
    workbook: data.title,
    exists: function (id) { return queriesById.has(id); },
    node: function (id) { return queryNode(queriesById.get(id), true); }
  });
  shell.explorer.setLayouts(explorerLayouts(), select);
  shell.start();
  renderDetails();
})();
