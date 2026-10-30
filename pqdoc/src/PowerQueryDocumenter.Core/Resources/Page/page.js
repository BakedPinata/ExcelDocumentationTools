// page.js: the page's shared parts (data, theme, selection, details panel, tabs and search).
// The views (the dependency graph) talk to it through window.pqdoc:
//   pqdoc.select(id)                 select a query (id = query name), or null
//   pqdoc.selection()                the selected id, or null
//   pqdoc.onSelectionChange(fn)      fn(id) after the selection changes
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

  var data = JSON.parse($("pqdoc-data").textContent);
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

  function escapeHtml(text) {
    return String(text).replace(/[&<>"]/g, function (c) {
      return c === "&" ? "&amp;" : c === "<" ? "&lt;" : c === ">" ? "&gt;" : "&quot;";
    });
  }

  // localStorage can throw when the page is opened from disk, so every access is guarded.
  function loadSetting(key) { try { return localStorage.getItem(key); } catch (e) { return null; } }
  function saveSetting(key, value) { try { localStorage.setItem(key, value); } catch (e) { /* not remembered */ } }

  var listeners = { selection: [], theme: [], view: [], search: [] };
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
    saveSetting("pqdoc-theme", next); // the user's choice now wins over the system setting
    applyTheme(next);
  });

  if (systemLight && systemLight.addEventListener) {
    systemLight.addEventListener("change", function (e) {
      var stored = loadSetting("pqdoc-theme");
      if (stored !== "light" && stored !== "dark") applyTheme(e.matches ? "light" : "dark");
    });
  }
  showTheme();

  // ---- Selection and the details panel ----

  var selection = null;
  var panelHeader = $("details-header");
  var panelBody = $("details-body");

  function select(id) {
    id = id || null;
    if (id !== null && !queriesById.has(id)) return false;
    if (id === selection) return true;
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
    panelHeader.innerHTML = "<h2>" + escapeHtml(query.id) + "</h2><p class=\"muted\" id=\"details-role\">" + ROLE_TEXT[query.role] +
      (query.inCycle ? " Part of a circular dependency." : "") + "</p>";
    panelBody.innerHTML =
      "<h3><span class=\"swatch upstream\" aria-hidden=\"true\"></span>Uses (" + uses.get(selection).length + ")</h3>" +
      "<div id=\"details-uses\">" + queryList(uses.get(selection)) + "</div>" +
      "<h3><span class=\"swatch downstream\" aria-hidden=\"true\"></span>Used by (" + usedBy.get(selection).length + ")</h3>" +
      "<div id=\"details-used-by\">" + queryList(usedBy.get(selection)) + "</div>" +
      "<p class=\"muted reach\" id=\"details-reach\">Including indirect links, " + countText(upstream) + " feed" + (upstream === 1 ? "s" : "") +
      " this query and " + countText(downstream) + " depend" + (downstream === 1 ? "s" : "") + " on it.</p>" +
      "<div class=\"actions\"><button type=\"button\" class=\"button\" id=\"details-clear\">Clear selection</button></div>";
  }

  panelBody.addEventListener("click", function (event) {
    if (event.target.closest("#details-clear")) { select(null); return; }
    var link = event.target.closest("[data-select]");
    if (!link) return;
    event.preventDefault();
    select(link.getAttribute("data-select"));
  });

  // ---- Showing, hiding and resizing the details panel ----
  //
  // The size is the panel's share of the layout (of its width, or of its height when the page is narrow and the
  // panel sits below the view), kept as the --details-size CSS variable. Until the user resizes it, the stylesheet's
  // defaults apply. The view needs no telling: the graph's SVG fills its box and watches its size.

  var layout = $("layout");
  var detailsPanel = $("details-panel");
  var splitter = $("details-splitter");
  var detailsToggle = $("details-toggle");
  var stacked = window.matchMedia ? window.matchMedia("(max-width: 900px)") : { matches: false };
  var MIN_DETAILS_SIZE = 0.2;
  var MAX_DETAILS_SIZE = 0.7;
  var KEY_STEP = 0.02;

  function clampSize(size) { return Math.min(MAX_DETAILS_SIZE, Math.max(MIN_DETAILS_SIZE, size)); }

  function currentDetailsSize() {
    var rect = layout.getBoundingClientRect();
    var panel = detailsPanel.getBoundingClientRect();
    return stacked.matches ? panel.height / rect.height : panel.width / rect.width;
  }

  // size: a share of the layout, or null for the stylesheet's default.
  function setDetailsSize(size, remember) {
    if (size === null) {
      layout.style.removeProperty("--details-size");
    } else {
      size = clampSize(size);
      layout.style.setProperty("--details-size", (size * 100).toFixed(1) + "%");
    }
    if (remember) saveSetting("pqdoc-details-size", size === null ? "" : size.toFixed(3));
    updateSplitter();
  }

  function updateSplitter() {
    splitter.setAttribute("aria-orientation", stacked.matches ? "horizontal" : "vertical");
    if (!detailsPanel.hidden) splitter.setAttribute("aria-valuenow", String(Math.round(currentDetailsSize() * 100)));
  }

  function setDetailsVisible(visible, remember) {
    detailsPanel.hidden = !visible;
    splitter.hidden = !visible;
    layout.classList.toggle("details-hidden", !visible);
    detailsToggle.setAttribute("aria-pressed", visible ? "true" : "false");
    if (remember) saveSetting("pqdoc-details-hidden", visible ? "false" : "true");
    updateSplitter();
  }

  detailsToggle.addEventListener("click", function () { setDetailsVisible(detailsPanel.hidden, true); });
  $("details-close").addEventListener("click", function () {
    setDetailsVisible(false, true);
    detailsToggle.focus(); // the button that had focus has gone
  });

  // Dragging. The panel is on the right (or at the bottom), so its size is the distance from the pointer to the
  // layout's far edge, less where in the splitter it was grabbed. Without that, a click would move the splitter out
  // from under the pointer, and the second click of a double-click would miss it.
  var grabOffset = 0;

  function sizeAtPointer(event) {
    var rect = layout.getBoundingClientRect();
    return stacked.matches ? (rect.bottom - event.clientY) / rect.height : (rect.right - event.clientX) / rect.width;
  }

  splitter.addEventListener("pointerdown", function (event) {
    if (event.button !== 0) return;
    grabOffset = sizeAtPointer(event) - currentDetailsSize();
    splitter.setPointerCapture(event.pointerId);
    splitter.classList.add("dragging");
    document.body.classList.add("resizing");
    event.preventDefault();
  });
  splitter.addEventListener("pointermove", function (event) {
    if (splitter.hasPointerCapture(event.pointerId)) setDetailsSize(sizeAtPointer(event) - grabOffset, false);
  });
  function endDrag(event) {
    if (!splitter.hasPointerCapture(event.pointerId)) return;
    splitter.releasePointerCapture(event.pointerId);
    splitter.classList.remove("dragging");
    document.body.classList.remove("resizing");
    setDetailsSize(sizeAtPointer(event) - grabOffset, true);
  }
  splitter.addEventListener("pointerup", endDrag);
  splitter.addEventListener("pointercancel", endDrag);
  splitter.addEventListener("dblclick", function () { setDetailsSize(null, true); });

  // Keyboard, as for a separator: the arrows move the splitter, Home and End make the panel smallest or largest.
  splitter.addEventListener("keydown", function (event) {
    var grow = stacked.matches ? { ArrowUp: 1, ArrowDown: -1 } : { ArrowLeft: 1, ArrowRight: -1 };
    var size;
    if (grow[event.key]) size = currentDetailsSize() + grow[event.key] * KEY_STEP;
    else if (event.key === "Home") size = MIN_DETAILS_SIZE;
    else if (event.key === "End") size = MAX_DETAILS_SIZE;
    else if (event.key === "Enter") size = null; // like double-click: back to the default
    else return;
    setDetailsSize(size, true);
    event.preventDefault();
  });

  if (stacked.addEventListener) stacked.addEventListener("change", updateSplitter);

  var storedSize = parseFloat(loadSetting("pqdoc-details-size"));
  setDetailsSize(isNaN(storedSize) ? null : storedSize, false);
  setDetailsVisible(loadSetting("pqdoc-details-hidden") !== "true", false);

  // ---- Tabs ----

  var tabs = [$("tab-graph")];
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

  // / jumps to the search box from anywhere, and Escape clears the selection unless it's emptying the box.
  document.addEventListener("keydown", function (event) {
    if (document.activeElement === searchBox) return;
    if (event.key === "/") {
      event.preventDefault();
      searchBox.focus();
    } else if (event.key === "Escape") {
      select(null);
    }
  });

  // ---- Start ----

  window.pqdoc = {
    data: data,
    queriesById: queriesById,
    select: select,
    selection: function () { return selection; },
    onSelectionChange: function (fn) { listeners.selection.push(fn); },
    theme: currentTheme,
    onThemeChange: function (fn) { listeners.theme.push(fn); },
    activeView: function () { return activeView; },
    onViewChange: function (fn) { listeners.view.push(fn); },
    matches: function () { return matches; },
    onSearchChange: function (fn) { listeners.search.push(fn); },
    clearSearch: clearSearch,
    uses: function (id) { return uses.get(id); },
    usedBy: function (id) { return usedBy.get(id); },
    upstream: function (id) { return chain(id, uses); },
    downstream: function (id) { return chain(id, usedBy); },
    countText: countText
  };

  showView("graph");
  renderDetails();
})();
