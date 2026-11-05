// shell.js: the page shell both tools' pages are built on (see shared/README.md). It owns the frame around the views:
// remembered settings, the theme, the tabs, saved views, the side panel and its splitter, Load folder and the source
// text. A tool's page.js creates it, fills the side panel, and builds the tool's own window.vbadoc or window.pqdoc on
// top, which is what the tool's views use.
//
//   var shell = ExcelDocumenterShell.create(options)
//
// options:
//   tool           "vbadoc" or "pqdoc". The data block is #<tool>-data, embedded source is in pre.<tool>-source, and
//                  every setting is stored as <tool>-<name>.
//   panel          the side panel's name in stored settings: <tool>-<panel>-size and <tool>-<panel>-hidden
//   rememberView   whether the open tab is remembered, as <tool>-view
//
// The shell gives the tool:
//   shell.data                         the page's data
//   shell.escapeHtml(text)             text made safe for HTML content and double-quoted attributes
//   shell.settings.load(name) / save(name, value) / remove(name)   remembered per browser; never throws
//   shell.theme() / onThemeChange(fn)  "light" or "dark"
//   shell.activeView() / showView(name, focus) / onViewChange(fn)   tab #tab-<name> shows view #view-<name>
//   shell.start()                      shows the remembered tab, or the first; call it once the tool is ready
//   shell.panel.element / header / body   the side panel, for the tool to fill
//   shell.panel.following()            true while the views change size because the side panel opened or closed with
//                                      the selection: a view then keeps still, rather than keeping its middle in the
//                                      middle, and makes sure the selection isn't under the panel
//   shell.explorer.setLayouts(layouts, select)   fills the explorer: layouts are [{ name, label, title, nodes }], the
//                                      first the default. With more than one, a switch above the tree chooses, and the
//                                      choice is remembered as <tool>-explorer-layout. nodes are { label, id, detail,
//                                      badge, title, className, folder, children, expanded }; folder: true draws a
//                                      folder before the label. Choosing a node with an id calls select(id)
//   shell.selectionChanged(id, other)  call on every selection (null for none), even of what's already selected, before
//                                      drawing it: the explorer marks the item, opening what holds it, and the side
//                                      panel opens or closes with it unless it's locked open. other is true for a
//                                      Ctrl+click (Cmd+click), which does the other thing from focus on click.
//                                      Returns whether the selection changed; if it didn't, its focus may have
//                                      (see onFocusChange)
//   shell.focused() / onFocusChange(fn)   whether the views show only the selection and what it's connected to; fn is
//                                      called when that changes without the selection changing
//   shell.focusOnClick() / setFocusOnClick(on)   the page's Focus on click setting, remembered as
//                                      <tool>-focus-on-click and on unless turned off; setting it applies it to the
//                                      current selection too
//   shell.items.init({ workbook, exists(id), node(id), describeFilter(ids) })   the workbook (favourites, the filter
//                                      and hidden items are remembered per workbook, as <tool>-favourites|<workbook>,
//                                      <tool>-filter|<workbook> and <tool>-hidden|<workbook>), whether an id is still
//                                      in it (others are ignored), the explorer node for one, and what the filter
//                                      shows, such as "5 of 70 modules"; call it before setLayouts. With
//                                      widelyUsed(share), the tool's explorer nodes for what's used by at least share%
//                                      of the others (most used first, and may hold children), the shell lists them in
//                                      a Widely used group, marks their rows, and offers the share in the Show… menu,
//                                      remembered as <tool>-widely-used-share
//   shell.filter.active() / has(id) / ids() / clear() / onFilterChange(fn)   the explorer's filter: the items ticked,
//                                      which the views show alone while any are. What a tick covers (a module's
//                                      members, say) is the tool's to say, through has()
//   shell.favourites.list() / has(id) / toggle(id) / onFavouritesChange(fn)   the favourites, in the order starred
//   shell.favourites.buttonHtml(id)    a star that adds or removes the favourite, for the side panel's header; the
//                                      explorer's rows have their own, and the shell keeps every star in step
//   shell.focusFavourites() / setFocusFavourites(on)   whether the views show only the favourites and what they use
//                                      (with the focused selection's, if any), remembered as <tool>-focus-favourites;
//                                      false while there are no favourites. Changes are told through onFocusChange
//   shell.hidden.list() / has(id) / toggle(id) / clear() / onHiddenChange(fn)   the items hidden from every view, in
//                                      the order hidden. What else goes with one (a module's members) is the tool's
//                                      to say
//   shell.choices.init({ sections })   the Show… menu in the explorer: what kinds of thing the tree and every view
//                                      show, remembered per tool as <tool>-show. sections are { title, choices: [{ id,
//                                      label }] } of check boxes, ticked (shown) unless turned off, or { title, radio:
//                                      name, choices: [{ value, label }] }, the first the default. Call it before
//                                      setLayouts; items.init's kept(id) then says whether the choices keep an item in
//                                      the tree
//   shell.choices.on(id) / value(name) / count() / key() / reset() / onChoicesChange(fn)   a box's state, a radio's
//                                      value, how many choices differ from the defaults, a string that changes with
//                                      them, and putting them all back
//   shell.hidden.buttonHtml(id)        a button that hides or shows the item, for the side panel's header, kept in step
//                                      with the explorer's
//   shell.hidden.noteHtml(id, hiddenNear)   the side panel's note when the item is hidden, or hiddenNear (a count of
//                                      the items it uses or is used by that are hidden) isn't 0; "" otherwise
//   Esc clears the selection, through the select function given to explorer.setLayouts, unless an open menu or a box
//   with text in it takes it first.
//   shell.source.init(source)          says where the source text comes from (call it once, before start):
//     mode         "embedded" (in the page), "folder" (Load folder) or "none" (there's no source to show)
//     attribute    the attribute that holds each embedded source's id, such as "data-module"
//     files()      -> [{ id, file, hash }]: the files Load folder looks for, with the first 16 hex digits of the
//                  SHA-256 each had when the page was made
//     decode(buffer) -> the text of a loaded file
//     note         added to every Load folder status, such as a decoding problem; "" for none
//     highlight(lines) -> one HTML string per line
//     noFiles      the status when the chosen folder holds none of the files
//     mismatch     what a changed file means, completing "so …", such as "lines may not match"
//     loaded()     called once Load folder has read a folder
//   shell.source.has(id) / lines(id)   the source text, as an array of lines
//   shell.source.codeHtml(id, first, last, marked)   lines first to last (default: all) as the numbered, highlighted
//                                      code view, with a button that copies those lines; lines from marked.first to
//                                      marked.last are marked
//   shell.references.listHtml(entries, empty)   a Uses or Used by list: each entry { id, name, detail, tags: [{ label,
//                                      title, className }], hidden, refs: [{ in, source, line, inName }] } once, its
//                                      name selecting it, and opening it lists refs: the lines where it's referenced,
//                                      with their text when source `source` is there. Choosing one selects `in` and
//                                      shows the line in the side panel's source, marked. empty is the text for none
//   shell.source.showLine(line)        scrolls the side panel's source to a line and marks it, if it's shown
//   shell.views.part(name, { save(), apply(value), ids(value) })   a part of every saved view, for one of the tool's
//                                      views: save() gives what to keep (JSON, or null for nothing), apply(value)
//                                      sets the view to it without drawing (value is undefined for a view without
//                                      it, which starts afresh), and ids(value) lists the items it mentions, so those
//                                      the workbook no longer has can be listed. name can't be one of the shell's own:
//                                      name, description, tab, filter, hidden, show, selection, focus, explorer or panel
//   shell.onViewApplied(fn)            called once a saved view has been opened, with the view's selection (an id, or
//                                      null), in place of onFilterChange, onHiddenChange and onChoicesChange, so the
//                                      tool draws its views once. The shell has already taken the selection, its focus
//                                      and the panels as the view has them, so the tool takes it as its own without
//                                      calling selectionChanged. Then onViewChange tells of the view's tab
//   A button with id "load-folder-prompt" anywhere in the page opens Load folder.
(function () {
  "use strict";

  function $(id) { return document.getElementById(id); }

  function escapeHtml(text) {
    return String(text).replace(/[&<>"]/g, function (c) {
      return c === "&" ? "&amp;" : c === "<" ? "&lt;" : c === ">" ? "&gt;" : "&quot;";
    });
  }

  function create(options) {
    var tool = options.tool;

    // ---- Settings ----
    //
    // localStorage is shared by every page opened from disk, so each tool's settings start with its name. It can
    // throw when the page is opened from disk, so every access is guarded.

    // Saving returns whether it worked, for what's worth saying when it doesn't (saved views).
    function saveSetting(name, value) {
      try { localStorage.setItem(tool + "-" + name, value); return true; } catch (e) { return false; }
    }

    var settings = {
      load: function (name) { try { return localStorage.getItem(tool + "-" + name); } catch (e) { return null; } },
      save: function (name, value) { saveSetting(name, value); },
      remove: function (name) { try { localStorage.removeItem(tool + "-" + name); } catch (e) { /* nothing stored */ } }
    };

    var listeners = { theme: [], view: [], focus: [], favourites: [], filter: [], hidden: [], choices: [], applied: [] };
    function emit(name, value) {
      listeners[name].forEach(function (fn) {
        try { fn(value); } catch (e) { console.error(e); }
      });
    }

    var data = JSON.parse($(tool + "-data").textContent);

    // The workbook's saved time is written in UTC, so the page is the same wherever it's made; show it in the viewer's
    // own time, keeping the UTC text if the browser can't.
    var savedTime = $("workbook-saved");
    if (savedTime) {
      try {
        var saved = new Date(savedTime.getAttribute("datetime"));
        if (!isNaN(saved.getTime())) savedTime.textContent = saved.toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
      } catch (e) { /* the UTC text stays */ }
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
      settings.save("theme", next); // the user's choice now wins over the system setting
      applyTheme(next);
    });

    if (systemLight && systemLight.addEventListener) {
      systemLight.addEventListener("change", function (e) {
        var stored = settings.load("theme");
        if (stored !== "light" && stored !== "dark") applyTheme(e.matches ? "light" : "dark");
      });
    }
    showTheme();

    // ---- Source text ----
    //
    // Embedded in the page (--embed-source), or read from the folder the user picks with Load folder, checking each
    // file against the hash taken when the page was made.

    var source = null;            // what the tool said in source.init
    var sources = new Map();      // id -> array of lines
    var highlighted = new Map();  // id -> array of highlighted HTML lines (cached)
    var statusBox = $("source-status");
    var statusText = $("source-status-text");
    var folderInput = $("load-folder");

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

    function initSource(config) {
      source = config;
      if (source.mode === "embedded") {
        statusBox.classList.add("embedded");
        document.querySelectorAll("pre." + tool + "-source").forEach(function (pre) {
          sources.set(pre.getAttribute(source.attribute), splitLines(pre.textContent));
        });
      } else if (source.mode === "folder") {
        setStatus("Source not loaded");
      } else {
        statusBox.hidden = true;
      }
    }

    function hex(buffer) {
      return Array.prototype.map.call(new Uint8Array(buffer), function (b) { return ("0" + b.toString(16)).slice(-2); }).join("");
    }

    // Reads the files from the folder the user picked. Only files directly in that folder count, so a .git folder or
    // other subfolders are ignored.
    function loadFolder(files) {
      var byName = new Map();
      Array.prototype.forEach.call(files, function (file) {
        var path = (file.webkitRelativePath || file.name).split("/");
        if (path.length <= 2) byName.set(path[path.length - 1].toLowerCase(), file);
      });

      var note = source.note || "";
      var missing = [];
      var changed = [];
      var found = 0;
      var reads = source.files().map(function (wanted) {
        var file = byName.get(wanted.file.toLowerCase());
        if (!file) { missing.push(wanted.file); return Promise.resolve(); }
        return file.arrayBuffer().then(function (buffer) {
          found++;
          sources.set(wanted.id, splitLines(source.decode(buffer)));
          highlighted.delete(wanted.id);
          if (!window.crypto || !crypto.subtle) return undefined;
          return crypto.subtle.digest("SHA-256", buffer).then(function (digest) {
            if (hex(digest).slice(0, 16) !== wanted.hash) changed.push(wanted.file);
          });
        });
      });

      return Promise.all(reads).then(function () {
        if (found === 0) {
          setStatus(source.noFiles, true);
        } else if (changed.length > 0) {
          setStatus(changed.length + " file(s) changed since this page was made, so " + source.mismatch + ". Run " + tool +
            " again. (" + changed.sort().join(", ") + ")" + note, true);
        } else if (missing.length > 0) {
          setStatus("Loaded " + found + " file(s); missing: " + missing.sort().join(", ") + note, true);
        } else {
          setStatus("Loaded " + found + " file(s)" + note, note !== "");
        }
        source.loaded();
      });
    }

    folderInput.addEventListener("change", function () {
      if (source && folderInput.files && folderInput.files.length > 0) loadFolder(folderInput.files);
    });

    // The side panel offers Load folder where the source would be.
    document.addEventListener("click", function (event) {
      if (event.target.closest && event.target.closest("#load-folder-prompt")) folderInput.click();
    });

    function highlightedLines(id) {
      if (!highlighted.has(id)) highlighted.set(id, source.highlight(sources.get(id)));
      return highlighted.get(id);
    }

    // The copy button sits in a row of no height above the code, which sticks to the top of what scrolls, and the
    // button sticks to its right, so it stays in view however far the code is scrolled either way.
    var COPY_ICON = "<svg class=\"icon copy-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><rect x=\"5.5\" y=\"5.5\" width=\"8.5\" height=\"8.5\" rx=\"1.5\"/><path d=\"M10.5 3.5V3A1.5 1.5 0 0 0 9 1.5H3.5A1.5 1.5 0 0 0 2 3v5.5A1.5 1.5 0 0 0 3.5 10H4\"/></svg>";
    var DONE_ICON = "<svg class=\"icon done-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\"><path d=\"M3 8.5l3.25 3.25L13 5\"/></svg>";

    function codeHtml(id, first, last, marked) {
      var lines = highlightedLines(id);
      first = first || 1;
      last = last || lines.length;
      var html = [];
      for (var n = first; n <= last; n++) {
        var mark = marked && n >= marked.first && n <= marked.last;
        html.push("<div class=\"line" + (mark ? " marked" : "") + "\" data-line=\"" + n + "\"><span class=\"ln\">" + n +
          "</span><span class=\"lt\">" + (lines[n - 1] || " ") + "</span></div>");
      }
      return "<div class=\"code-view\"><div class=\"code-tools\"><button type=\"button\" class=\"copy-code\" data-source=\"" +
        escapeHtml(id) + "\" data-first=\"" + first + "\" data-last=\"" + last + "\" title=\"Copy the code\" aria-label=\"Copy the code\">" +
        COPY_ICON + DONE_ICON + "<span class=\"copy-label\"></span></button></div>" +
        "<div class=\"code\" role=\"region\" aria-label=\"Source code\">" + html.join("") + "</div></div>";
    }

    // ---- Showing a line ----
    //
    // A reference's line, scrolled to the middle of the side panel's source and marked until something else is.

    function showLine(line) {
      var panelBox = $("side-panel");
      panelBox.querySelectorAll(".code .line.target").forEach(function (el) { el.classList.remove("target"); });
      var target = panelBox.querySelector(".code .line[data-line=\"" + line + "\"]");
      if (!target) return false;
      target.classList.add("target");
      target.scrollIntoView({ block: "center" });
      return true;
    }

    // ---- Uses / Used by ----
    //
    // Each item once, with tags for the kinds of link (the tool's), and the lines where it's referenced inside: the line
    // number, and its text when the source is there. Choosing a line selects what holds it and shows the line.

    var LINE_TEXT_LENGTH = 120;

    function referenceListHtml(entries, empty) {
      if (entries.length === 0) return "<p class=\"muted none\">" + escapeHtml(empty || "Nothing") + "</p>";
      return "<ul class=\"ref-list\">" + entries.map(function (entry) {
        var head = "<a href=\"#\" data-select=\"" + escapeHtml(entry.id) + "\">" + escapeHtml(entry.name) + "</a>" +
          (entry.detail ? " <span class=\"muted\">" + escapeHtml(entry.detail) + "</span>" : "") +
          (entry.tags || []).map(function (tag) {
            return " <span class=\"" + escapeHtml(tag.className || "ref-tag") + "\"" + (tag.title ? " title=\"" + escapeHtml(tag.title) + "\"" : "") +
              ">" + escapeHtml(tag.label) + "</span>";
          }).join("") +
          (entry.hidden ? " <span class=\"hidden-tag\" title=\"Hidden from the views\">hidden</span>" : "");
        var refs = entry.refs || [];
        if (refs.length === 0) return "<li class=\"ref-item\"><div class=\"ref-head\">" + head + "</div></li>";
        return "<li class=\"ref-item\"><details><summary class=\"ref-head\" title=\"Show where it's referenced\">" + head +
          " <span class=\"ref-count muted\">" + refs.length + (refs.length === 1 ? " line" : " lines") + "</span></summary>" +
          "<ol class=\"ref-lines\">" + refs.map(function (ref) {
            var text = sources.has(ref.source) ? String(sources.get(ref.source)[ref.line - 1] || "").trim() : null;
            if (text !== null && text.length > LINE_TEXT_LENGTH) text = text.slice(0, LINE_TEXT_LENGTH - 1) + "…";
            return "<li><button type=\"button\" class=\"ref-line\" data-ref-select=\"" + escapeHtml(ref["in"]) + "\" data-ref-line=\"" +
              ref.line + "\" title=\"Show line " + ref.line + (ref.inName ? " of " + escapeHtml(ref.inName) : "") + "\">" +
              "<span class=\"ref-ln\">" + ref.line + "</span>" + (text !== null ? "<code>" + escapeHtml(text) + "</code>" : "") +
              "</button></li>";
          }).join("") + "</ol></details></li>";
      }).join("") + "</ul>";
    }

    // Choosing a line: select what holds it (which draws its source), then show the line.
    document.addEventListener("click", function (event) {
      var button = event.target.closest && event.target.closest("[data-ref-line]");
      if (!button) return;
      event.preventDefault();
      selectItem(button.getAttribute("data-ref-select"), event.ctrlKey || event.metaKey);
      showLine(parseInt(button.getAttribute("data-ref-line"), 10));
    });

    // ---- Copying the source ----
    //
    // The lines shown, as the file has them, without line numbers. The clipboard API needs a secure context, which a
    // page opened from disk is in most browsers; failing that, the older copy command; failing both, the code is
    // selected for Ctrl+C.

    var announcer = $("announcer");

    function announce(text) {
      announcer.textContent = "";
      window.setTimeout(function () { announcer.textContent = text; }, 50);
    }

    function copyWithCommand(text) {
      var box = document.createElement("textarea");
      box.value = text;
      box.setAttribute("readonly", "");
      box.style.position = "fixed";
      box.style.top = "0";
      box.style.opacity = "0";
      var focused = document.activeElement;
      document.body.appendChild(box);
      box.select();
      var copied = false;
      try { copied = document.execCommand("copy"); } catch (e) { copied = false; }
      document.body.removeChild(box);
      if (focused && focused.focus) focused.focus();
      return copied;
    }

    function copyText(text) {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text).then(function () { return true; }, function () { return copyWithCommand(text); });
      }
      return Promise.resolve(copyWithCommand(text));
    }

    function selectCode(button) {
      var code = button.closest(".code-view").querySelector(".code");
      var range = document.createRange();
      range.selectNodeContents(code);
      var selected = window.getSelection();
      selected.removeAllRanges();
      selected.addRange(range);
    }

    function showCopied(button, text) {
      button.classList.add("copied");
      button.querySelector(".copy-label").textContent = text;
      announce(text);
      window.clearTimeout(button.copiedTimer);
      button.copiedTimer = window.setTimeout(function () {
        button.classList.remove("copied");
        button.querySelector(".copy-label").textContent = "";
      }, 2000);
    }

    document.addEventListener("click", function (event) {
      var button = event.target.closest && event.target.closest(".copy-code");
      if (!button) return;
      var lines = sources.get(button.getAttribute("data-source")) || [];
      var text = lines.slice(Number(button.getAttribute("data-first")) - 1, Number(button.getAttribute("data-last"))).join("\n");
      copyText(text).then(function (copied) {
        if (copied) {
          showCopied(button, "Copied");
        } else {
          selectCode(button);
          showCopied(button, "Press Ctrl+C to copy");
        }
      });
    });

    // ---- Showing, hiding and resizing the panels ----
    //
    // The explorer sits before the views (left, or above them on a narrow screen) and the side panel after them (right,
    // or below). Every view shares both, so they all share each panel's size and whether it's shown. A panel's size is
    // its share of the layout (of its width, or of its height when the page is narrow and the panels are stacked), kept
    // as a CSS variable. Until the user resizes it, the stylesheets' defaults apply. The views need no telling: an SVG
    // fills its box and keeps its zoom, and a scrolling box scrolls.

    var layout = $("layout");
    var stacked = window.matchMedia ? window.matchMedia("(max-width: 900px)") : { matches: false };
    var KEY_STEP = 0.02;

    // config: element, splitter, toggle, close (ids); end: true when the panel is after the views; sizeVariable;
    // setting (stored as <setting>-size and <setting>-hidden); hiddenClass; min and max shares; visibleByDefault().
    function resizablePanel(config) {
      var element = $(config.element);
      var splitter = $(config.splitter);
      var toggle = $(config.toggle);

      function clampSize(size) { return Math.min(config.max, Math.max(config.min, size)); }

      function currentSize() {
        var rect = layout.getBoundingClientRect();
        var box = element.getBoundingClientRect();
        return stacked.matches ? box.height / rect.height : box.width / rect.width;
      }

      // size: a share of the layout, or null for the stylesheets' default.
      var chosenSize = null;
      function setSize(size, remember) {
        if (size === null) {
          layout.style.removeProperty(config.sizeVariable);
        } else {
          size = clampSize(size);
          layout.style.setProperty(config.sizeVariable, (size * 100).toFixed(1) + "%");
        }
        chosenSize = size === null ? null : Math.round(size * 1000) / 1000;
        if (remember) settings.save(config.setting + "-size", size === null ? "" : size.toFixed(3));
        updateSplitter();
      }

      function updateSplitter() {
        splitter.setAttribute("aria-orientation", stacked.matches ? "horizontal" : "vertical");
        if (!element.hidden) splitter.setAttribute("aria-valuenow", String(Math.round(currentSize() * 100)));
      }

      function setVisible(visible, remember) {
        element.hidden = !visible;
        splitter.hidden = !visible;
        layout.classList.toggle(config.hiddenClass, !visible);
        toggle.setAttribute("aria-pressed", visible ? "true" : "false");
        if (remember) settings.save(config.setting + "-hidden", visible ? "false" : "true");
        updateSplitter();
      }

      // Opening or closing by hand. A panel that doesn't remember whether it's shown (the side panel, which follows
      // the selection) tells the shell instead.
      function setByHand(visible) {
        setVisible(visible, config.rememberVisible !== false);
        if (config.changedByHand) config.changedByHand(visible);
      }

      toggle.addEventListener("click", function () { setByHand(element.hidden); });
      $(config.close).addEventListener("click", function () {
        setByHand(false);
        toggle.focus(); // the button that had focus has gone
      });

      // Dragging. The size is the distance from the pointer to the panel's own edge of the layout, less where in the
      // splitter it was grabbed. Without that, a click would move the splitter out from under the pointer, and the
      // second click of a double-click would miss it.
      var grabOffset = 0;

      function sizeAtPointer(event) {
        var rect = layout.getBoundingClientRect();
        if (stacked.matches) return config.end ? (rect.bottom - event.clientY) / rect.height : (event.clientY - rect.top) / rect.height;
        return config.end ? (rect.right - event.clientX) / rect.width : (event.clientX - rect.left) / rect.width;
      }

      splitter.addEventListener("pointerdown", function (event) {
        if (event.button !== 0) return;
        grabOffset = sizeAtPointer(event) - currentSize();
        splitter.setPointerCapture(event.pointerId);
        splitter.classList.add("dragging");
        document.body.classList.add("resizing");
        event.preventDefault();
      });
      splitter.addEventListener("pointermove", function (event) {
        if (splitter.hasPointerCapture(event.pointerId)) setSize(sizeAtPointer(event) - grabOffset, false);
      });
      function endDrag(event) {
        if (!splitter.hasPointerCapture(event.pointerId)) return;
        splitter.releasePointerCapture(event.pointerId);
        splitter.classList.remove("dragging");
        document.body.classList.remove("resizing");
        setSize(sizeAtPointer(event) - grabOffset, true);
      }
      splitter.addEventListener("pointerup", endDrag);
      splitter.addEventListener("pointercancel", endDrag);
      splitter.addEventListener("dblclick", function () { setSize(null, true); });

      // Keyboard, as for a separator: the arrows move the splitter, Home and End make the panel smallest or largest.
      splitter.addEventListener("keydown", function (event) {
        var grow = stacked.matches
          ? (config.end ? { ArrowUp: 1, ArrowDown: -1 } : { ArrowDown: 1, ArrowUp: -1 })
          : (config.end ? { ArrowLeft: 1, ArrowRight: -1 } : { ArrowRight: 1, ArrowLeft: -1 });
        var size;
        if (grow[event.key]) size = currentSize() + grow[event.key] * KEY_STEP;
        else if (event.key === "Home") size = config.min;
        else if (event.key === "End") size = config.max;
        else if (event.key === "Enter") size = null; // like double-click: back to the default
        else return;
        setSize(size, true);
        event.preventDefault();
      });

      if (stacked.addEventListener) stacked.addEventListener("change", updateSplitter);

      var storedSize = parseFloat(settings.load(config.setting + "-size"));
      setSize(isNaN(storedSize) ? null : storedSize, false);
      var storedHidden = config.rememberVisible === false ? null : settings.load(config.setting + "-hidden");
      setVisible(storedHidden === null ? config.visibleByDefault() : storedHidden !== "true", false);
      return {
        element: element, setVisible: setVisible, isVisible: function () { return !element.hidden; },
        size: function () { return chosenSize; }, setSize: setSize
      };
    }

    // ---- The side panel follows the selection ----
    //
    // It opens when something is selected and closes when the selection is cleared, giving the views the room, unless
    // it's locked open. The lock is remembered per tool (<tool>-<panel>-locked); whether it's shown isn't, since the
    // selection decides. Closing it by hand also unlocks it, because a lock keeps it open.

    var lockButton = $("side-panel-lock");
    var locked = settings.load(options.panel + "-locked") === "true";
    var selected = null;

    function setLocked(on) {
      locked = on;
      lockButton.setAttribute("aria-pressed", on ? "true" : "false");
      lockButton.title = on
        ? "Unlock: close the panel when nothing is selected"
        : "Keep the panel open when nothing is selected";
      settings.save(options.panel + "-locked", on ? "true" : "false");
    }

    var sidePanel = resizablePanel({
      element: "side-panel", splitter: "side-panel-splitter", toggle: "side-panel-toggle", close: "side-panel-close",
      end: true, sizeVariable: "--side-panel-size", setting: options.panel, hiddenClass: "side-panel-hidden",
      min: 0.2, max: 0.7, visibleByDefault: function () { return locked; }, rememberVisible: false,
      changedByHand: function (visible) { if (!visible && locked) setLocked(false); }
    });
    var panel = sidePanel.element;
    lockButton.setAttribute("aria-pressed", locked ? "true" : "false");
    if (locked) lockButton.title = "Unlock: close the panel when nothing is selected";
    lockButton.addEventListener("click", function () { setLocked(!locked); });

    // Set while the views take their new size, which they're told of by a ResizeObserver in the next frame; cleared
    // the frame after.
    var following = false;

    // ---- Focus on click ----
    //
    // One setting for the whole page: while it's on, selecting something focuses every view on it, showing only it and
    // what it's connected to. A Ctrl+click (Cmd+click on a Mac) does the other thing for that one selection, so both
    // are always a click away. The views ask focused() whenever they draw.

    var focusOnClick = settings.load("focus-on-click") !== "false";
    var focused = false;

    function setFocusOnClick(on) {
      focusOnClick = !!on;
      settings.save("focus-on-click", focusOnClick ? "true" : "false");
      focused = selected !== null && focusOnClick;
      emit("focus", focused);
    }

    function selectionChanged(id, other) {
      var next = id === undefined ? null : id;
      var nextFocused = next !== null && focusOnClick !== !!other;
      if (next === selected) {
        if (nextFocused !== focused) {
          focused = nextFocused;
          emit("focus", focused);
        }
        return false;
      }
      selected = next;
      focused = nextFocused;
      reveal(selected);
      if (locked || sidePanel.isVisible() === (selected !== null)) return true;
      keepViewsStill();
      sidePanel.setVisible(selected !== null, false);
      return true;
    }

    // While the panels change size for the selection, or for a saved view, which draws the views to fit already.
    function keepViewsStill() {
      following = true;
      window.requestAnimationFrame(function () {
        window.requestAnimationFrame(function () { following = false; });
      });
    }

    // Esc clears the selection wherever it's pressed. An open menu takes it first, and so does a box with text in it,
    // which empties instead; this runs in the capture phase, before their own handlers close or empty them.
    document.addEventListener("keydown", function (event) {
      if (event.key !== "Escape" || selected === null) return;
      if (document.querySelector("details.dropdown[open]")) return;
      var target = event.target;
      if (target && target.matches && target.matches("input:not([type=checkbox]), textarea, select") && target.value) return;
      selectItem(null);
    }, true);

    // Drop-down menus (<details class="dropdown">), in the explorer and the views' toolbars, close when you press
    // anywhere outside them, including starting to pan or drag a view, or press Escape. Opening one therefore closes any
    // other.
    document.addEventListener("pointerdown", function (event) {
      document.querySelectorAll("details.dropdown[open]").forEach(function (d) { if (!d.contains(event.target)) d.open = false; });
    }, true);
    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape") document.querySelectorAll("details.dropdown[open]").forEach(function (d) { d.open = false; });
    });

    // Open by default on a wide screen, where there's room beside the views; on a narrow one it would push them down.
    var explorerPanel = resizablePanel({
      element: "explorer", splitter: "explorer-splitter", toggle: "explorer-toggle", close: "explorer-close",
      end: false, sizeVariable: "--explorer-size", setting: "explorer", hiddenClass: "explorer-hidden",
      min: 0.1, max: 0.4, visibleByDefault: function () { return !stacked.matches; }
    });

    // ---- Explorer tree ----
    //
    // The tool gives the tree as nodes: { label, id, detail, badge, title, className, folder, children, expanded }. A
    // node with an id is something the page documents, and choosing it calls the tool's select; one without is a
    // group, drawn as a folder when it is one. Groups start open and other nodes with children start closed. It's a
    // tree in the ARIA sense: one item is in the tab order, the arrow keys move through what's showing, and Enter
    // chooses. The tool can give more than one layout of the same items, such as folders and kinds.

    var treeBox = $("explorer-tree");
    var filterBox = $("explorer-filter");
    var emptyNote = $("explorer-empty");
    var layoutSwitch = $("explorer-layouts");
    var itemsById = new Map();    // id -> the items showing it: in the layout, and in Favourites
    var labels = new WeakMap();   // item -> its label in lower case, for the filter
    var selectItem = function () { };
    var selectedItems = [];
    var revealedId = null;        // what's selected, so a new layout can show it too
    var layouts = [];
    var activeLayout = null;

    // Drawn for this page rather than taken from an icon set, whose licences ask for attribution (see shared/README.md).
    var FOLDER_ICON = "<svg class=\"tree-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\">" +
      "<path d=\"M1.75 3.25h4.1l1.5 1.6h6.9v7.9H1.75z\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.3\" stroke-linejoin=\"round\"/></svg>";

    function treeNode(node, depth) {
      var li = document.createElement("li");
      var isItem = node.id !== undefined && node.id !== null;
      li.setAttribute("role", "treeitem");
      li.setAttribute("aria-level", String(depth + 1));
      li.tabIndex = -1;
      li.className = (isItem ? "item" : "group") + (node.folder ? " folder" : "") + (node.className ? " " + node.className : "");
      labels.set(li, String(node.label).toLowerCase());

      // The tooltip is the row's, so it doesn't show over the rows inside a group.
      var row = document.createElement("div");
      row.className = "tree-row";
      if (node.title) row.title = node.title;
      row.style.setProperty("--depth", String(depth));
      row.innerHTML = "<input type=\"checkbox\" class=\"tree-check\" tabindex=\"-1\" aria-label=\"Show in the views\">" +
        "<span class=\"tree-twisty\" aria-hidden=\"true\"></span>" + (node.folder ? FOLDER_ICON : "") +
        "<span class=\"tree-label\">" + escapeHtml(node.label) + "</span>" +
        (node.detail ? "<span class=\"tree-detail\">" + escapeHtml(node.detail) + "</span>" : "") +
        (node.badge ? "<span class=\"tree-badge\">" + escapeHtml(node.badge) + "</span>" : "") +
        (isItem ? hideHtml(node.id, "tree-hide") + starHtml(node.id, "tree-star") : "");
      li.appendChild(row);

      if (isItem) {
        li.dataset.id = node.id;
        li.setAttribute("aria-selected", "false");
        if (!itemsById.has(node.id)) itemsById.set(node.id, []);
        itemsById.get(node.id).push(li);
      }

      if (node.children && node.children.length > 0) {
        var open = node.expanded !== undefined ? node.expanded : !isItem;
        li.setAttribute("aria-expanded", open ? "true" : "false");
        li.classList.toggle("collapsed", !open);
        var group = document.createElement("ul");
        group.setAttribute("role", "group");
        node.children.forEach(function (child) { group.appendChild(treeNode(child, depth + 1)); });
        li.appendChild(group);
      }
      return li;
    }

    function setLayouts(given, select) {
      layouts = given;
      selectItem = select;
      var stored = settings.load("explorer-layout");
      activeLayout = layouts.some(function (l) { return l.name === stored; }) ? stored : layouts[0].name;
      layoutSwitch.innerHTML = layouts.map(function (l) {
        return "<button type=\"button\" role=\"radio\" data-layout=\"" + escapeHtml(l.name) + "\"" +
          (l.title ? " title=\"" + escapeHtml(l.title) + "\"" : "") + ">" + escapeHtml(l.label) + "</button>";
      }).join("");
      layoutSwitch.hidden = layouts.length < 2;
      showLayout();
    }

    // Draws the chosen layout, keeping the filter and what's selected.
    function showLayout() {
      var layout = layouts.filter(function (l) { return l.name === activeLayout; })[0];
      layoutSwitch.querySelectorAll("button").forEach(function (b) {
        b.setAttribute("aria-checked", b.dataset.layout === activeLayout ? "true" : "false");
      });
      itemsById.clear();
      selectedItems = [];
      treeBox.innerHTML = "";
      favouritesItem = null;
      hiddenItem = null;
      widelyUsedItem = null;
      layout.nodes.forEach(function (node) { treeBox.appendChild(treeNode(node, 0)); });
      var first = treeBox.querySelector("li");
      if (first) first.tabIndex = 0;
      renderFavourites();
      renderWidelyUsed();
      renderHidden();
      markHidden();
      markWidelyUsed();
      markLeftOut();
      applyFilter();
      updateChecks();
      reveal(revealedId);
    }

    // ---- Favourites ----
    //
    // Any number, starred from the explorer or the side panel's header, remembered per workbook in the browser, and
    // listed at the top of the explorer in every layout, in the order they were starred. A favourite that's no longer
    // in the workbook is ignored. Every star is a button with data-favourite="<id>", which the shell keeps in step.

    var STAR_ICON = "<svg class=\"icon star-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\">" +
      "<path d=\"M8 1.8l1.9 3.95 4.35.55-3.2 3 .8 4.3L8 11.5l-3.85 2.1.8-4.3-3.2-3 4.35-.55z\"/></svg>";
    var favouriteSource = null; // from items.init
    var favourites = [];        // ids in the order starred, including any no longer in the workbook
    var favouritesItem = null;  // the Favourites group in the tree, or null when there are none
    var favouritesOpen = true;

    function favouritesKey() { return "favourites|" + favouriteSource.workbook; }

    function loadIds(name) {
      try {
        var stored = JSON.parse(settings.load(name) || "[]");
        return Array.isArray(stored) ? stored.filter(function (id) { return typeof id === "string"; }) : [];
      } catch (e) { return []; }
    }

    function initItems(source) {
      favouriteSource = source;
      favourites = loadIds(favouritesKey());
      ticked = new Set(loadIds(filterKey()));
      hiddenIds = loadIds(hiddenKey());
      savedViews = loadViews();
      updateFilter();
      updateNotes();
      updateViewsSummary();
      if (source.widelyUsed) addShareSetting();
    }

    function favouriteList() {
      return favouriteSource ? favourites.filter(function (id) { return favouriteSource.exists(id); }) : [];
    }

    function isFavourite(id) { return favouriteList().indexOf(id) >= 0; }

    function starHtml(id, className) {
      var on = isFavourite(id);
      return "<button type=\"button\" class=\"favourite-toggle " + className + "\" data-favourite=\"" + escapeHtml(id) + "\"" +
        (className === "tree-star" ? " tabindex=\"-1\"" : "") + " aria-pressed=\"" + on + "\" aria-label=\"Favourite\" title=\"" +
        (on ? "Remove from favourites" : "Add to favourites") + "\">" + STAR_ICON + "</button>";
    }

    function toggleFavourite(id) {
      var at = favourites.indexOf(id);
      if (at >= 0) favourites.splice(at, 1); else favourites.push(id);
      if (favouriteSource) settings.save(favouritesKey(), JSON.stringify(favourites));
      document.querySelectorAll("[data-favourite]").forEach(function (button) {
        var on = isFavourite(button.getAttribute("data-favourite"));
        button.setAttribute("aria-pressed", on ? "true" : "false");
        button.title = on ? "Remove from favourites" : "Add to favourites";
      });
      renderFavourites();
      markHidden();
      applyFilter();
      updateChecks();
      updateNotes();
      reveal(revealedId, true);
      emit("favourites", favouriteList());
      if (focusFavouritesOn) emit("focus", focused); // the favourites the views focus on changed
    }

    // ---- Focus on favourites ----
    //
    // A page-wide setting, turned on and off from the Favourites group's row: while it's on and there are favourites,
    // every view shows only them and what they use, as well as the focused selection and what it's connected to.

    var focusFavouritesOn = settings.load("focus-favourites") === "true";

    function focusFavourites() { return focusFavouritesOn && favouriteList().length > 0; }

    function setFocusFavourites(on) {
      focusFavouritesOn = !!on;
      settings.save("focus-favourites", focusFavouritesOn ? "true" : "false");
      document.querySelectorAll("[data-focus-favourites]").forEach(function (button) {
        button.setAttribute("aria-pressed", focusFavouritesOn ? "true" : "false");
      });
      updateNotes();
      emit("focus", focused);
    }

    // ---- Hiding ----
    //
    // Hiding takes an item out of every view; the tool says what goes with it (a module's members). Hidden with the
    // button on an explorer row or in the side panel's header, remembered per workbook, and never silent: a hidden
    // item stays in the layout, marked, and is listed in a Hidden group at the foot of the explorer, to bring back one
    // at a time or all at once; every view's toolbar counts them; and the side panel says when the selection is hidden
    // or what it's linked to is. Every hide button has data-hide="<id>", which the shell keeps in step.

    // Drawn for this page: an eye, struck through.
    var HIDE_ICON = "<svg class=\"icon hide-icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\">" +
      "<path d=\"M1.75 8S4 4.25 8 4.25 14.25 8 14.25 8 12 11.75 8 11.75 1.75 8 1.75 8z\"/><circle cx=\"8\" cy=\"8\" r=\"1.75\"/>" +
      "<path d=\"M2.75 13.25l10.5-10.5\"/></svg>";
    var hiddenIds = [];     // in the order hidden, including any no longer in the workbook
    var hiddenItem = null;  // the Hidden group in the tree, or null when nothing is hidden
    var hiddenOpen = false;

    function hiddenKey() { return "hidden|" + favouriteSource.workbook; }

    function hiddenList() {
      return favouriteSource ? hiddenIds.filter(function (id) { return favouriteSource.exists(id); }) : [];
    }

    function isHidden(id) { return hiddenList().indexOf(id) >= 0; }

    function hideTitle(on) { return on ? "Show it again in the views" : "Hide it from the views"; }

    function hideHtml(id, className) {
      var on = isHidden(id);
      return "<button type=\"button\" class=\"hide-toggle " + className + "\" data-hide=\"" + escapeHtml(id) + "\"" +
        (className === "tree-hide" ? " tabindex=\"-1\"" : "") + " aria-pressed=\"" + on + "\" aria-label=\"Hidden\" title=\"" +
        hideTitle(on) + "\">" + HIDE_ICON + "</button>";
    }

    function updateHideButtons() {
      document.querySelectorAll("[data-hide]").forEach(function (button) {
        var on = isHidden(button.getAttribute("data-hide"));
        button.setAttribute("aria-pressed", on ? "true" : "false");
        button.title = hideTitle(on);
      });
    }

    function setHidden(ids) {
      hiddenIds = ids;
      if (favouriteSource) settings.save(hiddenKey(), JSON.stringify(hiddenIds));
      updateHideButtons();
      renderHidden();
      markHidden();
      updateHideAll();
      applyFilter();
      updateChecks();
      updateNotes();
      reveal(revealedId, true);
      emit("hidden", hiddenList());
    }

    function toggleHidden(id) {
      var next = hiddenIds.filter(function (other) { return other !== id; });
      if (next.length === hiddenIds.length) next.push(id);
      setHidden(next);
    }

    // Marks the layout's hidden items, and what they hold.
    function markHidden() {
      var hidden = new Set(hiddenList());
      treeBox.querySelectorAll("li[data-id]").forEach(function (li) {
        var on = false;
        if (!hiddenItem || !hiddenItem.contains(li)) {
          for (var at = li; at && !on; at = parentItem(at)) on = at.dataset.id !== undefined && hidden.has(at.dataset.id);
        }
        li.classList.toggle("hidden-from-views", on);
      });
    }

    // The Hidden group: each hidden item once, at the foot of the tree, closed to begin with. Its rows have no
    // checkboxes, since what's hidden isn't shown whatever's ticked, and their hide buttons bring them back.
    function renderHidden() {
      if (hiddenItem) {
        hiddenOpen = hiddenItem.getAttribute("aria-expanded") === "true";
        removeGroup(hiddenItem);
        hiddenItem = null;
      }
      var ids = hiddenList();
      if (ids.length === 0 || !favouriteSource) return;
      hiddenItem = treeNode({
        label: "Hidden",
        detail: String(ids.length),
        title: "Hidden from the views",
        className: "hidden-items",
        expanded: hiddenOpen,
        children: ids.map(function (id) {
          var node = Object.assign({}, favouriteSource.node(id), { id: id });
          delete node.children;
          return node;
        })
      }, 0);
      hiddenItem.querySelectorAll(".tree-check").forEach(function (box) { box.remove(); });
      hiddenItem.querySelector(":scope > .tree-row").insertAdjacentHTML("beforeend",
        "<button type=\"button\" class=\"link-button tree-action\" data-unhide-all tabindex=\"-1\" title=\"Show every hidden item in the views again\">Show all</button>");
      treeBox.appendChild(hiddenItem);
    }

    // Opens the explorer at the Hidden group, from a toolbar's count.
    function showHiddenGroup() {
      if (!hiddenItem) return;
      explorerPanel.setVisible(true, false);
      setExpanded(hiddenItem, true);
      focusItem(hiddenItem);
      hiddenItem.scrollIntoView({ block: "nearest" });
    }

    function noteHtml(id, hiddenNear) {
      var parts = [];
      if (isHidden(id)) {
        parts.push("Hidden from the views. <button type=\"button\" class=\"link-button\" data-hide=\"" + escapeHtml(id) + "\">Show it</button>");
      }
      if (hiddenNear > 0) {
        parts.push(hiddenNear + (hiddenNear === 1 ? " item it's linked to is" : " items it's linked to are") + " hidden from the views" +
          (hiddenList().length > 0 ? " (<button type=\"button\" class=\"link-button\" data-show-hidden>list</button>)" : "") + ".");
      }
      return parts.length === 0 ? "" : "<p class=\"hidden-note\" role=\"note\">" + parts.join(" ") + "</p>";
    }

    // ---- Widely used ----
    //
    // What's used by at least a share of the others (the tool counts), listed in a Widely used group near the foot of
    // the explorer, most used first, with Hide all, and marked where it is in the layout. The share is a setting in the
    // Show… menu, remembered per tool. Nothing is hidden to begin with.

    // Drawn for this page: a hub, with spokes out to what uses it.
    var WIDE_ICON = "<svg class=\"icon\" viewBox=\"0 0 16 16\" aria-hidden=\"true\" focusable=\"false\">" +
      "<circle cx=\"8\" cy=\"8\" r=\"2.25\"/><path d=\"M8 1.75v2.5M8 11.75v2.5M1.75 8h2.5M11.75 8h2.5M3.6 3.6l1.75 1.75M10.65 10.65l1.75 1.75M12.4 3.6l-1.75 1.75M5.35 10.65L3.6 12.4\"/></svg>";
    var DEFAULT_SHARE = 20;
    var widelyUsedItem = null;      // the Widely used group, or null when nothing is
    var widelyUsedOpen = false;
    var widelyUsedTops = [];        // the group's top-level ids, which Hide all hides
    var widelyUsedTitles = new Map(); // id -> how widely it's used, for its marker
    var share = clampShare(parseFloat(settings.load("widely-used-share")));

    function clampShare(value) { return isFinite(value) && value >= 1 && value <= 100 ? Math.round(value) : DEFAULT_SHARE; }

    function renderWidelyUsed() {
      if (widelyUsedItem) {
        widelyUsedOpen = widelyUsedItem.getAttribute("aria-expanded") === "true";
        removeGroup(widelyUsedItem);
        widelyUsedItem = null;
      }
      widelyUsedTops = [];
      widelyUsedTitles = new Map();
      if (!favouriteSource || !favouriteSource.widelyUsed) return;
      var nodes = favouriteSource.widelyUsed(share);
      (function collect(list) {
        list.forEach(function (node) {
          widelyUsedTitles.set(node.id, node.title || "Widely used");
          if (node.children) collect(node.children);
        });
      })(nodes);
      widelyUsedTops = nodes.map(function (node) { return node.id; });
      if (nodes.length === 0) return;
      widelyUsedItem = treeNode({
        label: "Widely used",
        detail: String(nodes.length),
        title: "Used by at least " + share + "% of the others (the share is in the Show… menu)",
        className: "widely-used-items",
        expanded: widelyUsedOpen,
        children: nodes
      }, 0);
      widelyUsedItem.querySelector(":scope > .tree-row").insertAdjacentHTML("beforeend",
        "<button type=\"button\" class=\"link-button tree-action\" data-hide-widely-used tabindex=\"-1\"></button>");
      treeBox.insertBefore(widelyUsedItem, hiddenItem);
      updateHideAll();
    }

    // Hide all hides the group's top-level items (a vbadoc module takes its members); once they're all hidden, it shows
    // them again.
    function allWidelyUsedHidden() { return widelyUsedTops.length > 0 && widelyUsedTops.every(isHidden); }

    function updateHideAll() {
      var button = widelyUsedItem && widelyUsedItem.querySelector("[data-hide-widely-used]");
      if (!button) return;
      var all = allWidelyUsedHidden();
      button.textContent = all ? "Show all" : "Hide all";
      button.title = all ? "Show every widely used item in the views again" : "Hide every widely used item from the views";
    }

    function hideAllWidelyUsed() {
      var rest = hiddenIds.filter(function (id) { return widelyUsedTops.indexOf(id) < 0; });
      setHidden(allWidelyUsedHidden() ? rest : rest.concat(widelyUsedTops));
    }

    // A marker after the name of every widely used item, wherever it is (but not in its own group).
    function markWidelyUsed() {
      treeBox.querySelectorAll("li[data-id]").forEach(function (li) {
        var row = li.querySelector(":scope > .tree-row");
        var marker = row.querySelector(":scope > .tree-wide");
        var title = widelyUsedItem && widelyUsedItem.contains(li) ? null : widelyUsedTitles.get(li.dataset.id);
        if (!title) { if (marker) marker.remove(); return; }
        if (!marker) {
          marker = document.createElement("span");
          marker.className = "tree-wide";
          marker.innerHTML = WIDE_ICON;
          row.querySelector(".tree-label").insertAdjacentElement("afterend", marker);
        }
        marker.title = "Widely used: " + title.charAt(0).toLowerCase() + title.slice(1);
      });
    }

    // The share, as a section of the Show… menu (it's not a choice of what to show, so it isn't counted).
    function addShareSetting() {
      var sections = showMenu.querySelector(".show-sections");
      if (!sections || $("explorer-widely-used-share")) return;
      sections.insertAdjacentHTML("beforeend", "<section><h3>Widely used</h3><label class=\"share\">Used by at least " +
        "<input type=\"number\" id=\"explorer-widely-used-share\" min=\"1\" max=\"100\" step=\"1\" value=\"" + share + "\"> %" +
        " of the others</label></section>");
      $("explorer-widely-used-share").addEventListener("input", function (event) {
        var value = parseFloat(event.target.value);
        if (!isFinite(value) || value < 1 || value > 100) return;
        share = clampShare(value);
        settings.save("widely-used-share", String(share));
        renderWidelyUsed();
        markWidelyUsed();
        applyFilter();
        updateChecks();
        reveal(revealedId, true);
      });
    }

    // ---- The Show… menu ----
    //
    // What kinds of thing the tree and every view show, chosen in one place: the tool gives the sections and says
    // whether the choices keep an item (items.init's kept). Remembered per tool, not per workbook, as the views' own
    // Show… menus were. Never silent: the button counts the choices in effect, and so does a note in every view's
    // toolbar, with Show everything.

    var showBox = $("explorer-show");
    var showMenu = $("explorer-show-menu");
    var showSummary = showBox.querySelector("summary");
    var choiceSections = [];
    var choicesOff = new Set();   // check boxes turned off
    var radioValues = {};         // radio group -> value, when not the default

    function radioDefault(name) {
      var section = choiceSections.filter(function (s) { return s.radio === name; })[0];
      return section ? section.choices[0].value : null;
    }

    function choiceOn(id) { return !choicesOff.has(id); }

    function choiceValue(name) { return radioValues[name] !== undefined ? radioValues[name] : radioDefault(name); }

    function choicesCount() { return choicesOff.size + Object.keys(radioValues).length; }

    function choicesKey() {
      return Array.from(choicesOff).sort().join("|") + "\u0000" +
        Object.keys(radioValues).sort().map(function (name) { return name + "=" + radioValues[name]; }).join("|");
    }

    function initChoices(options) {
      choiceSections = options.sections;
      var ids = [];
      var radios = {};
      choiceSections.forEach(function (s) {
        if (s.radio) radios[s.radio] = s.choices.map(function (c) { return c.value; });
        else s.choices.forEach(function (c) { ids.push(c.id); });
      });
      var stored = {};
      try { stored = JSON.parse(settings.load("show") || "{}") || {}; } catch (e) { stored = {}; }
      choicesOff = new Set((Array.isArray(stored.off) ? stored.off : []).filter(function (id) { return ids.indexOf(id) >= 0; }));
      radioValues = {};
      Object.keys(radios).forEach(function (name) {
        var value = stored.radios && stored.radios[name];
        if (radios[name].indexOf(value) > 0) radioValues[name] = value;
      });
      // A section of check boxes can be ticked or cleared at once, to pick a few things quickly.
      showMenu.innerHTML = "<div class=\"show-sections\">" + choiceSections.map(function (s, i) {
        var quick = s.radio ? "" : " <span class=\"show-quick\"><button type=\"button\" class=\"link-button\" data-show-section-all=\"" + i +
          "\" title=\"Tick everything in " + escapeHtml(s.title) + "\">All</button> · <button type=\"button\" class=\"link-button\" data-show-section-none=\"" + i +
          "\" title=\"Clear everything in " + escapeHtml(s.title) + ", to tick just what you want\">None</button></span>";
        return "<section><h3><span>" + escapeHtml(s.title) + "</span>" + quick + "</h3>" + s.choices.map(function (c) {
          return s.radio
            ? "<label><input type=\"radio\" name=\"show-" + escapeHtml(s.radio) + "\" data-radio=\"" + escapeHtml(s.radio) + "\" value=\"" + escapeHtml(c.value) + "\"> " + escapeHtml(c.label) + "</label>"
            : "<label><input type=\"checkbox\" data-choice=\"" + escapeHtml(c.id) + "\"> " + escapeHtml(c.label) + "</label>";
        }).join("") + "</section>";
      }).join("") + "</div>" +
        "<div class=\"actions\"><button type=\"button\" class=\"button\" data-show-reset>Show everything</button>" +
        "<button type=\"button\" class=\"button\" data-show-nothing title=\"Clear every box, to tick just what you want\">Show nothing</button></div>";
      showBox.hidden = false;
      updateChoices();
    }

    function saveChoices() {
      settings.save("show", JSON.stringify({ off: Array.from(choicesOff), radios: radioValues }));
      updateChoices();
      markLeftOut();
      applyFilter();
      emit("choices");
    }

    function updateChoices() {
      showMenu.querySelectorAll("[data-choice]").forEach(function (box) { box.checked = choiceOn(box.getAttribute("data-choice")); });
      showMenu.querySelectorAll("[data-radio]").forEach(function (radio) { radio.checked = radio.value === choiceValue(radio.getAttribute("data-radio")); });
      var count = choicesCount();
      showSummary.textContent = count > 0 ? "Show… (" + count + ")" : "Show…";
      showSummary.title = count > 0
        ? count + " of the Show… choices leave things out of the explorer and the views"
        : "What to show, in the explorer and every view";
      showBox.classList.toggle("active", count > 0);
      showNotes.forEach(function (note) {
        note.hidden = count === 0;
        note.firstChild.textContent = "Show… (" + count + ")";
      });
    }

    showMenu.addEventListener("change", function (event) {
      var input = event.target;
      if (input.hasAttribute("data-choice")) {
        if (input.checked) choicesOff.delete(input.getAttribute("data-choice")); else choicesOff.add(input.getAttribute("data-choice"));
      } else if (input.hasAttribute("data-radio")) {
        var name = input.getAttribute("data-radio");
        if (input.value === radioDefault(name)) delete radioValues[name]; else radioValues[name] = input.value;
      } else {
        return;
      }
      saveChoices();
    });

    // Ticks or clears every box in a section.
    function setSection(index, on) {
      choiceSections[index].choices.forEach(function (c) { if (on) choicesOff.delete(c.id); else choicesOff.add(c.id); });
      saveChoices();
    }

    showMenu.addEventListener("click", function (event) {
      var all = event.target.closest("[data-show-section-all]");
      var none = event.target.closest("[data-show-section-none]");
      if (all) setSection(Number(all.getAttribute("data-show-section-all")), true);
      else if (none) setSection(Number(none.getAttribute("data-show-section-none")), false);
      else if (event.target.closest("[data-show-nothing]")) {
        choiceSections.forEach(function (s) { if (!s.radio) s.choices.forEach(function (c) { choicesOff.add(c.id); }); });
        saveChoices();
      }
    });

    function resetChoices() {
      choicesOff = new Set();
      radioValues = {};
      saveChoices();
    }

    // The menu gets the room below it, and moves left if it would run off the window. Opening it here, rather than on
    // the "toggle" event, which comes a frame late, lets it measure before it's drawn.
    function openShowMenuHere() {
      showBox.open = true;
      showMenu.style.maxHeight = "";
      showMenu.style.left = "";
      var box = showMenu.getBoundingClientRect();
      showMenu.style.maxHeight = Math.max(120, window.innerHeight - box.top - 12) + "px";
      var over = box.right - (window.innerWidth - 12);
      if (over > 0) showMenu.style.left = -Math.min(over, Math.max(0, box.left - 12)) + "px";
    }

    showSummary.addEventListener("click", function (event) {
      if (showBox.open) return;
      event.preventDefault();
      openShowMenuHere();
    });

    // Opens the menu from a toolbar's note, opening the explorer first if it's closed.
    function openShowMenu() {
      explorerPanel.setVisible(true, false);
      openShowMenuHere();
      var first = showMenu.querySelector("input");
      if (first) first.focus();
    }

    // Marks the layout's items the choices leave out, and the folders and groups left with nothing to show. The
    // Favourites and Hidden groups are the user's own lists, so they keep everything.
    function markLeftOut() {
      var kept = favouriteSource && favouriteSource.kept;
      function mark(li) {
        if (li === favouritesItem || li === hiddenItem || li === widelyUsedItem) return true;
        var group = li.querySelector(":scope > ul");
        var anyShown = false;
        if (group) Array.prototype.forEach.call(group.children, function (child) { if (mark(child)) anyShown = true; });
        var shown = li.dataset.id !== undefined ? !kept || kept(li.dataset.id) : !group || anyShown;
        li.classList.toggle("left-out", !shown);
        return shown;
      }
      Array.prototype.forEach.call(treeBox.children, mark);
    }

    // ---- Notes in every view's toolbar ----
    //
    // What limits what the views show, each with its way out: Focus on favourites, the filter (below) and hiding.

    var favouritesNotes = [];
    var hiddenNotes = [];
    var showNotes = [];
    document.querySelectorAll(".view-toolbar").forEach(function (toolbar) {
      var favouritesNote = document.createElement("span");
      favouritesNote.className = "toolbar-note view-favourites-note";
      favouritesNote.hidden = true;
      favouritesNote.innerHTML = "<span>Favourites and what they use</span> <button type=\"button\" class=\"link-button\" data-focus-favourites-off>Show all</button>";
      toolbar.appendChild(favouritesNote);
      favouritesNotes.push(favouritesNote);
    });

    function updateNotes() {
      var count = hiddenList().length;
      hiddenNotes.forEach(function (note) {
        note.hidden = count === 0;
        note.firstChild.textContent = count + " hidden";
      });
      favouritesNotes.forEach(function (note) { note.hidden = !focusFavourites(); });
    }

    // One handler for every hide button, the Hidden group's Show all, the notes' links and the Favourites group's Focus.
    document.addEventListener("click", function (event) {
      if (!event.target.closest) return;
      var hide = event.target.closest("[data-hide]");
      if (hide) { event.preventDefault(); toggleHidden(hide.getAttribute("data-hide")); return; }
      if (event.target.closest("[data-unhide-all]")) setHidden([]);
      else if (event.target.closest("[data-show-hidden]")) showHiddenGroup();
      else if (event.target.closest("[data-focus-favourites]")) setFocusFavourites(!focusFavouritesOn);
      else if (event.target.closest("[data-focus-favourites-off]")) setFocusFavourites(false);
      else if (event.target.closest("[data-show-reset]")) resetChoices();
      else if (event.target.closest("[data-hide-widely-used]")) hideAllWidelyUsed();
      else if (event.target.closest("[data-show-open]")) openShowMenu();
    });

    // Takes a group drawn by the shell (Favourites, Hidden) out of the tree, with its items.
    function removeGroup(group) {
      group.querySelectorAll("li[data-id]").forEach(function (li) {
        var list = itemsById.get(li.dataset.id) || [];
        list.splice(list.indexOf(li), 1);
        if (list.length === 0) itemsById.delete(li.dataset.id);
        var selectedAt = selectedItems.indexOf(li);
        if (selectedAt >= 0) selectedItems.splice(selectedAt, 1);
      });
      var hadFocusStop = group.querySelector("li[tabindex=\"0\"]") || group.tabIndex === 0;
      group.remove();
      if (hadFocusStop && treeBox.querySelector("li")) treeBox.querySelector("li").tabIndex = 0;
    }

    // ---- The explorer's filter ----
    //
    // Ticking an item's box adds it to the filter, and while anything is ticked the views show only what's ticked; the
    // tool says what a tick covers (has()). A box is apart from the name, which still selects, and from the selection:
    // Esc clears the selection, not the filter. Ticking a folder or group ticks the items in it, and an item's own box
    // ticks it with all it holds (a module's members), whose boxes then show ticked. Remembered per workbook; never
    // silent, with a bar in the explorer and a note in every view's toolbar, each with Clear.

    var ticked = new Set();     // ids ticked, including any no longer in the workbook
    var filterBar = $("explorer-filter-bar");
    var filterNotes = [];
    document.querySelectorAll(".view-toolbar").forEach(function (toolbar) {
      var note = document.createElement("span");
      note.className = "toolbar-note view-filter-note";
      note.hidden = true;
      note.innerHTML = "<span></span> <button type=\"button\" class=\"link-button\" data-filter-clear>Clear</button>";
      toolbar.appendChild(note);
      filterNotes.push(note);
      var hiddenNote = document.createElement("span");
      hiddenNote.className = "toolbar-note view-hidden-note";
      hiddenNote.hidden = true;
      hiddenNote.innerHTML = "<button type=\"button\" class=\"link-button\" data-show-hidden title=\"List them in the explorer, to show them again\"></button>";
      toolbar.appendChild(hiddenNote);
      hiddenNotes.push(hiddenNote);
      var showNote = document.createElement("span");
      showNote.className = "toolbar-note view-show-note";
      showNote.hidden = true;
      showNote.innerHTML = "<button type=\"button\" class=\"link-button\" data-show-open title=\"Change them in the explorer's Show… menu\"></button> " +
        "<button type=\"button\" class=\"link-button\" data-show-reset>Show everything</button>";
      toolbar.appendChild(showNote);
      showNotes.push(showNote);
    });

    function filterKey() { return "filter|" + favouriteSource.workbook; }

    function filterIds() {
      return favouriteSource ? Array.from(ticked).filter(function (id) { return favouriteSource.exists(id); }) : [];
    }

    function filterActive() { return filterIds().length > 0; }

    function setTicked(ids) {
      ticked = new Set(ids);
      if (favouriteSource) settings.save(filterKey(), JSON.stringify(Array.from(ticked)));
      updateFilter();
      emit("filter", filterIds());
    }

    function updateFilter() {
      var active = filterActive();
      var text = active && favouriteSource.describeFilter ? "Filtered: " + favouriteSource.describeFilter(filterIds()) : "Filtered";
      filterBar.hidden = !active;
      $("explorer-filter-text").textContent = text;
      filterNotes.forEach(function (note) {
        note.hidden = !active;
        note.firstChild.textContent = text;
      });
      updateChecks();
    }

    // Items in the tree directly under a row: its child items, or for a group, the first item on each branch.
    function topItems(li) {
      var found = [];
      var group = li.querySelector(":scope > ul");
      if (!group) return found;
      Array.prototype.forEach.call(group.children, function (child) {
        if (child.dataset.id !== undefined) found.push(child);
        else found = found.concat(topItems(child));
      });
      return found;
    }

    function itemsBelow(li) { return Array.prototype.slice.call(li.querySelectorAll("li[data-id]")); }

    // Whether an item is ticked, itself or through an item holding it.
    function isTicked(li) {
      for (var at = li; at; at = parentItem(at)) if (at.dataset.id !== undefined && ticked.has(at.dataset.id)) return true;
      return false;
    }

    function updateChecks() {
      treeBox.querySelectorAll("li").forEach(function (li) {
        var box = li.querySelector(":scope > .tree-row > .tree-check");
        if (!box) return;
        var isItem = li.dataset.id !== undefined;
        var own = isItem && ticked.has(li.dataset.id);
        var through = isItem && !own && isTicked(li);
        var below = itemsBelow(li);
        var tops = isItem ? [] : topItems(li);
        var all = isItem ? own || through : tops.length > 0 && tops.every(isTicked);
        box.checked = all;
        box.disabled = through;
        box.indeterminate = !all && below.some(function (item) { return ticked.has(item.dataset.id); });
        box.title = through ? "Shown in the views with what holds it" : all ? "Shown in the views. Click to take it out of the filter"
          : "Show only the ticked items in the views";
      });
    }

    function toggleTick(li) {
      var box = li.querySelector(":scope > .tree-row > .tree-check");
      if (!box || box.disabled) return;
      var next = new Set(ticked);
      var under = itemsBelow(li).map(function (item) { return item.dataset.id; });
      var tops = topItems(li);
      if (li.dataset.id !== undefined) {
        // An item ticked brings all it holds, so their own ticks aren't needed.
        if (next.has(li.dataset.id)) next.delete(li.dataset.id);
        else { next.add(li.dataset.id); under.forEach(function (id) { next.delete(id); }); }
      } else if (tops.length > 0 && tops.every(isTicked)) {
        // A group ticked throughout is unticked, with everything in it. (Not box.checked: a click has already changed it.)
        under.forEach(function (id) { next.delete(id); });
      } else {
        under.forEach(function (id) { next.delete(id); });
        tops.forEach(function (item) { next.add(item.dataset.id); });
      }
      setTicked(Array.from(next));
    }

    document.addEventListener("click", function (event) {
      if (event.target.closest && event.target.closest("[data-filter-clear]")) setTicked([]);
    });

    // ---- Expand and Collapse all ----
    //
    // Expand all opens every folder and group, but not each item's own children (a module's members), which on a large
    // project would list every member. Collapse all closes everything.

    $("explorer-expand-all").addEventListener("click", function () {
      treeBox.querySelectorAll("li.group").forEach(function (li) { setExpanded(li, true); });
    });
    $("explorer-collapse-all").addEventListener("click", function () {
      treeBox.querySelectorAll("li[aria-expanded]").forEach(function (li) { setExpanded(li, false); });
    });

    // The Favourites group: each favourite once, without what it holds. Left out when there are none; drawn again when
    // they change, keeping the group open or closed as it was.
    function renderFavourites() {
      if (favouritesItem) {
        favouritesOpen = favouritesItem.getAttribute("aria-expanded") !== "false";
        removeGroup(favouritesItem);
        favouritesItem = null;
      }
      var ids = favouriteList();
      if (ids.length === 0 || !favouriteSource) return;
      favouritesItem = treeNode({
        label: "Favourites",
        detail: String(ids.length),
        className: "favourites",
        expanded: favouritesOpen,
        children: ids.map(function (id) {
          var node = Object.assign({}, favouriteSource.node(id), { id: id });
          delete node.children;
          return node;
        })
      }, 0);
      favouritesItem.querySelector(":scope > .tree-row").insertAdjacentHTML("beforeend",
        "<button type=\"button\" class=\"tree-action focus-favourites\" data-focus-favourites aria-pressed=\"" + focusFavouritesOn +
        "\" title=\"Focus on favourites: show only them and what they use, in every view\">Focus</button>");
      treeBox.insertBefore(favouritesItem, treeBox.firstChild);
    }

    document.addEventListener("click", function (event) {
      var button = event.target.closest && event.target.closest("[data-favourite]");
      if (!button) return;
      event.preventDefault();
      toggleFavourite(button.getAttribute("data-favourite"));
    });

    layoutSwitch.addEventListener("click", function (event) {
      var button = event.target.closest("button[data-layout]");
      if (!button || button.dataset.layout === activeLayout) return;
      activeLayout = button.dataset.layout;
      settings.save("explorer-layout", activeLayout);
      showLayout();
    });

    function hasChildren(li) { return li.hasAttribute("aria-expanded"); }

    function isOpen(li) { return li.getAttribute("aria-expanded") === "true" || li.classList.contains("open-for-filter"); }

    function setExpanded(li, open) {
      if (!hasChildren(li)) return;
      li.setAttribute("aria-expanded", open ? "true" : "false");
      li.classList.toggle("collapsed", !open);
    }

    function parentItem(li) {
      var parent = li.parentElement ? li.parentElement.closest("li") : null;
      return parent && treeBox.contains(parent) ? parent : null;
    }

    // What's showing: items not filtered out whose ancestors are all open, in order.
    function visibleItems() {
      return Array.prototype.filter.call(treeBox.querySelectorAll("li"), function (li) {
        if (li.hidden || li.classList.contains("left-out")) return false;
        for (var at = parentItem(li); at; at = parentItem(at)) {
          if (at.hidden || at.classList.contains("left-out") || !isOpen(at)) return false;
        }
        return true;
      });
    }

    function focusItem(li) {
      treeBox.querySelectorAll("li[tabindex=\"0\"]").forEach(function (other) { other.tabIndex = -1; });
      li.tabIndex = 0;
      li.focus();
    }

    function choose(li, other) {
      if (li.dataset.id !== undefined) selectItem(li.dataset.id, other);
      else setExpanded(li, !isOpen(li));
    }

    treeBox.addEventListener("click", function (event) {
      if (event.target.closest(".favourite-toggle, .hide-toggle, .tree-action")) return; // buttons with handlers of their own
      var li = event.target.closest("li");
      if (li && treeBox.contains(li) && event.target.closest(".tree-check")) {
        toggleTick(li);
        updateChecks(); // the browser has already changed the box; put it right
        focusItem(li);  // the keyboard carries on from its row, not the box
        return;
      }
      if (!li || !treeBox.contains(li)) return;
      focusItem(li);
      if (event.target.closest(".tree-twisty") && hasChildren(li)) setExpanded(li, !isOpen(li));
      else choose(li, event.ctrlKey || event.metaKey);
    });

    treeBox.addEventListener("keydown", function (event) {
      var li = event.target.closest("li");
      if (!li) return;
      var items = visibleItems();
      var at = items.indexOf(li);
      var handled = true;
      if (event.key === "ArrowDown") {
        if (at + 1 < items.length) focusItem(items[at + 1]);
      } else if (event.key === "ArrowUp") {
        if (at > 0) focusItem(items[at - 1]); else filterBox.focus();
      } else if (event.key === "Home") {
        focusItem(items[0]);
      } else if (event.key === "End") {
        focusItem(items[items.length - 1]);
      } else if (event.key === "ArrowRight") {
        if (hasChildren(li) && !isOpen(li)) setExpanded(li, true);
        else if (hasChildren(li) && items[at + 1] && parentItem(items[at + 1]) === li) focusItem(items[at + 1]);
      } else if (event.key === "ArrowLeft") {
        if (hasChildren(li) && isOpen(li) && !li.classList.contains("open-for-filter")) setExpanded(li, false);
        else if (parentItem(li)) focusItem(parentItem(li));
      } else if (event.key === "Enter") {
        choose(li, event.ctrlKey || event.metaKey);
      } else if (event.key === " ") {
        toggleTick(li);
      } else {
        handled = false;
      }
      if (handled) {
        event.preventDefault();
        event.stopPropagation();
      }
    });

    // The filter keeps every item whose name contains the text, with what holds it, opening that while it filters.
    // Clearing it puts back what was open before.
    function applyFilter() {
      var wanted = filterBox.value.trim().toLowerCase();
      treeBox.classList.toggle("filtering", wanted !== "");
      treeBox.querySelectorAll("li").forEach(function (li) {
        li.hidden = false;
        li.classList.remove("match", "open-for-filter");
      });
      if (wanted === "") {
        emptyNote.hidden = true;
        return;
      }

      var shown = 0;
      function filter(li) {
        var matches = labels.get(li).indexOf(wanted) >= 0;
        var childMatches = false;
        var group = li.querySelector(":scope > ul");
        if (group) {
          Array.prototype.forEach.call(group.children, function (child) { if (filter(child)) childMatches = true; });
        }
        li.hidden = !matches && !childMatches;
        li.classList.toggle("match", matches);
        li.classList.toggle("open-for-filter", childMatches);
        if (matches && li.dataset.id !== undefined && !li.classList.contains("left-out")) shown++;
        return matches || childMatches;
      }
      Array.prototype.forEach.call(treeBox.children, filter);
      emptyNote.hidden = shown > 0;
    }

    filterBox.addEventListener("input", applyFilter);
    filterBox.addEventListener("keydown", function (event) {
      if (event.key === "ArrowDown") {
        var first = visibleItems()[0];
        if (first) focusItem(first);
        event.preventDefault();
      } else if (event.key === "Escape" && filterBox.value !== "") {
        filterBox.value = "";
        applyFilter();
        event.stopPropagation(); // emptying the box isn't clearing the selection
      }
    });

    // Marks the selected item, opening what holds it and scrolling it into view, without taking the focus.
    // An item can show in Favourites as well as in the layout: both are marked, the one in the layout is opened up to,
    // and the tree only scrolls if neither is in view, so choosing a favourite leaves the tree where it is. quiet: only
    // mark them, for when the tree is drawn again.
    function reveal(id, quiet) {
      revealedId = id === undefined ? null : id;
      selectedItems.forEach(function (li) { li.setAttribute("aria-selected", "false"); });
      selectedItems = revealedId === null ? [] : (itemsById.get(revealedId) || []).slice();
      if (selectedItems.length === 0) return;
      selectedItems.forEach(function (li) { li.setAttribute("aria-selected", "true"); });
      var main = selectedItems.filter(function (li) {
        return (!favouritesItem || !favouritesItem.contains(li)) && (!hiddenItem || !hiddenItem.contains(li)) &&
          (!widelyUsedItem || !widelyUsedItem.contains(li));
      })[0] || selectedItems[0];
      if (quiet) return;
      for (var at = parentItem(main); at; at = parentItem(at)) setExpanded(at, true);
      treeBox.querySelectorAll("li[tabindex=\"0\"]").forEach(function (other) { other.tabIndex = -1; });
      main.tabIndex = 0;
      if (selectedItems.some(rowInView)) return;
      var row = main.querySelector(".tree-row");
      if (row && row.offsetParent !== null) row.scrollIntoView({ block: "nearest" });
    }

    function rowInView(li) {
      var row = li.querySelector(".tree-row");
      if (!row || row.offsetParent === null) return false;
      var box = row.getBoundingClientRect();
      var view = treeBox.getBoundingClientRect();
      return box.bottom > view.top && box.top < view.bottom;
    }

    // ---- Saved views ----
    //
    // A view is a named arrangement of the page, with a description: the tab, the selection and whether it's focused,
    // Focus on click and Focus on favourites, the explorer's filter, the hidden items, the Show… choices and the
    // widely used share, whether the explorer and the side panel are shown, their sizes, the explorer's layout and the
    // side panel's lock, and what each of the tool's views adds through views.part (the graph's positions, say).
    // Favourites are the user's own, so they aren't part of one, and nor are the theme and the views' own preferences.
    // The Views menu in the top bar saves the page as a view, opens one, saves over one, and renames, describes or
    // deletes it. Views are kept per workbook in the browser, as <tool>-views|<workbook>, and Export and Import carry
    // them in a file, <tool>-views.json, which works offline in a page opened from disk. The file is JSON with a format
    // version and the workbook's name, written with sorted keys and 2-space indentation so its diffs are small.
    //
    // Opening a view sets everything at once, the selection included, telling the tool through onViewApplied rather
    // than the selection's, the filter's, hiding's and the choices' own events, so each view is drawn once. What it
    // mentions that the workbook no longer has is left out and listed, in the page and on the console, never treated
    // as an error. A view saved before it kept something (the panels, say) leaves that as it is.

    var VIEWS_FORMAT = 1;
    var VIEW_NAME_LENGTH = 80;
    var SHELL_PARTS = ["name", "description", "tab", "filter", "hidden", "show", "selection", "focus", "explorer", "panel"];
    var viewParts = [];          // { name, save(), apply(value), ids(value) }, from the tool
    var savedViews = [];         // in the menu's order
    var currentView = null;      // the name of the view last opened or saved, or null
    var currentArrangement = null; // how the page was arranged then, to tell whether it's changed since
    var undoState = null;        // while Undo is offered: the list as it was, and what to say once it's put back
    var editing = null;          // null, "new", or the view being renamed or described
    var viewsBox = $("views");
    var viewsSummary = viewsBox.querySelector("summary");
    var viewsList = $("views-list");
    var viewsForm = $("views-form");
    var viewsName = $("views-name");
    var viewsDescription = $("views-description");
    var viewsError = $("views-error");
    var viewsStatus = $("views-status");
    var viewsInput = $("views-import");
    var noticeBox = $("page-notice");

    function viewsKey() { return "views|" + favouriteSource.workbook; }

    function stringList(value) { return Array.isArray(value) ? value.filter(function (id) { return typeof id === "string"; }) : []; }

    // A view as read from the browser or a file: its name and description checked and the shell's parts made sound.
    // The tool's parts are kept as they are, for their apply() to check.
    function cleanView(raw) {
      if (!raw || typeof raw !== "object" || Array.isArray(raw) || typeof raw.name !== "string" || raw.name.trim() === "") return null;
      var view = {};
      Object.keys(raw).forEach(function (key) { view[key] = raw[key]; });
      view.name = raw.name.trim().slice(0, VIEW_NAME_LENGTH);
      view.description = typeof raw.description === "string" ? raw.description : "";
      if (typeof raw.tab !== "string") delete view.tab;
      view.filter = stringList(raw.filter);
      view.hidden = stringList(raw.hidden);
      var show = raw.show && typeof raw.show === "object" ? raw.show : {};
      view.show = { off: stringList(show.off), radios: {} };
      if (show.radios && typeof show.radios === "object") {
        Object.keys(show.radios).forEach(function (name) {
          if (typeof show.radios[name] === "string") view.show.radios[name] = show.radios[name];
        });
      }
      if (typeof show.share === "number" && isFinite(show.share)) view.show.share = show.share;
      var selection = raw.selection;
      if (selection && typeof selection === "object" && typeof selection.id === "string") {
        view.selection = { id: selection.id, focused: selection.focused === true };
      } else {
        delete view.selection;
      }
      // What a view saved before these were kept doesn't have is left as it is when it's opened.
      view.focus = pick(raw.focus, { onClick: "boolean", favourites: "boolean" });
      view.explorer = pick(raw.explorer, { shown: "boolean", size: "size", layout: "string" });
      view.panel = pick(raw.panel, { shown: "boolean", size: "size", locked: "boolean" });
      ["focus", "explorer", "panel"].forEach(function (key) { if (view[key] === null) delete view[key]; });
      return view;
    }

    // The fields of an object that have the type given ("size": a share of the layout, or null for the default), or
    // null when there are none.
    function pick(raw, types) {
      if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
      var picked = {};
      var any = false;
      Object.keys(types).forEach(function (key) {
        var value = raw[key];
        var ok = types[key] === "size" ? value === null || (typeof value === "number" && isFinite(value)) : typeof value === types[key];
        if (ok) { picked[key] = value; any = true; }
      });
      return any ? picked : null;
    }

    function loadViews() {
      try {
        var stored = JSON.parse(settings.load(viewsKey()) || "[]");
        return Array.isArray(stored) ? stored.map(cleanView).filter(function (view) { return view !== null; }) : [];
      } catch (e) { return []; }
    }

    // Returns whether the browser kept them.
    function storeViews() { return !favouriteSource || saveSetting(viewsKey(), JSON.stringify(savedViews)); }

    // How the page is arranged now, as a view without a name.
    function captureView() {
      var view = {
        tab: activeView,
        filter: Array.from(ticked).sort(),
        hidden: hiddenIds.slice(),
        show: { off: Array.from(choicesOff).sort(), radios: Object.assign({}, radioValues) },
        focus: { onClick: focusOnClick, favourites: focusFavouritesOn },
        explorer: { shown: explorerPanel.isVisible(), size: explorerPanel.size() },
        panel: { shown: sidePanel.isVisible(), size: sidePanel.size(), locked: locked }
      };
      if (favouriteSource && favouriteSource.widelyUsed) view.show.share = share;
      if (activeLayout !== null) view.explorer.layout = activeLayout;
      if (selected !== null) view.selection = { id: selected, focused: focused };
      viewParts.forEach(function (part) {
        try {
          var value = part.save();
          if (value !== undefined && value !== null) view[part.name] = value;
        } catch (e) { console.error(e); }
      });
      return view;
    }

    // Objects with their keys in order, so the same views always give the same text.
    function sortedKeys(value) {
      if (Array.isArray(value)) return value.map(sortedKeys);
      if (value && typeof value === "object") {
        var sorted = {};
        Object.keys(value).sort().forEach(function (key) { sorted[key] = sortedKeys(value[key]); });
        return sorted;
      }
      return value;
    }

    function arrangementOf(view) {
      var copy = Object.assign({}, view);
      delete copy.name;
      delete copy.description;
      return JSON.stringify(sortedKeys(copy));
    }

    // The items a view mentions that the workbook doesn't have.
    function missingFrom(view) {
      if (!favouriteSource) return [];
      var ids = view.filter.concat(view.hidden, view.selection ? [view.selection.id] : []);
      viewParts.forEach(function (part) {
        if (!part.ids || view[part.name] === undefined) return;
        try { ids = ids.concat(part.ids(view[part.name]) || []); } catch (e) { console.error(e); }
      });
      var missing = new Set();
      ids.forEach(function (id) { if (typeof id === "string" && !favouriteSource.exists(id)) missing.add(id); });
      return Array.from(missing).sort();
    }

    function listOf(ids) {
      var most = 8;
      return ids.slice(0, most).join(", ") + (ids.length > most ? " and " + (ids.length - most) + " more" : "");
    }

    function itemsText(count) { return count + (count === 1 ? " item" : " items"); }
    function viewsText(count) { return count + (count === 1 ? " view" : " views"); }

    // Sets the page as the view has it, all at once, then lets the tool take the selection and draw.
    function applyView(view) {
      var boxes = [];
      choiceSections.forEach(function (s) { if (!s.radio) s.choices.forEach(function (c) { boxes.push(c.id); }); });
      choicesOff = new Set(view.show.off.filter(function (id) { return boxes.indexOf(id) >= 0; }));
      radioValues = {};
      Object.keys(view.show.radios).forEach(function (name) {
        var section = choiceSections.filter(function (s) { return s.radio === name; })[0];
        var values = section ? section.choices.map(function (c) { return c.value; }) : [];
        if (values.indexOf(view.show.radios[name]) > 0) radioValues[name] = view.show.radios[name];
      });
      settings.save("show", JSON.stringify({ off: Array.from(choicesOff), radios: radioValues }));
      hiddenIds = view.hidden.slice();
      settings.save(hiddenKey(), JSON.stringify(hiddenIds));
      ticked = new Set(view.filter);
      settings.save(filterKey(), JSON.stringify(Array.from(ticked)));
      var shareChanged = false;
      if (view.show.share !== undefined && favouriteSource && favouriteSource.widelyUsed && clampShare(view.show.share) !== share) {
        share = clampShare(view.show.share);
        settings.save("widely-used-share", String(share));
        if ($("explorer-widely-used-share")) $("explorer-widely-used-share").value = String(share);
        shareChanged = true;
      }

      var focus = view.focus || {};
      if (focus.onClick !== undefined) {
        focusOnClick = focus.onClick;
        settings.save("focus-on-click", focusOnClick ? "true" : "false");
      }
      if (focus.favourites !== undefined) {
        focusFavouritesOn = focus.favourites;
        settings.save("focus-favourites", focusFavouritesOn ? "true" : "false");
        document.querySelectorAll("[data-focus-favourites]").forEach(function (button) {
          button.setAttribute("aria-pressed", focusFavouritesOn ? "true" : "false");
        });
      }

      // The selection is the shell's to set here, without the side panel following it: the view says where the panels
      // are. The views keep still while the panels change, since they're drawn for their new size.
      keepViewsStill();
      selected = view.selection && favouriteSource && favouriteSource.exists(view.selection.id) ? view.selection.id : null;
      focused = selected !== null && view.selection.focused;
      var explorer = view.explorer || {};
      if (explorer.shown !== undefined) explorerPanel.setVisible(explorer.shown, true);
      if (explorer.size !== undefined) explorerPanel.setSize(explorer.size, true);
      var layoutChanged = explorer.layout !== undefined && explorer.layout !== activeLayout &&
        layouts.some(function (l) { return l.name === explorer.layout; });
      if (layoutChanged) {
        activeLayout = explorer.layout;
        settings.save("explorer-layout", activeLayout);
      }
      var side = view.panel || {};
      if (side.locked !== undefined) setLocked(side.locked);
      if (side.size !== undefined) sidePanel.setSize(side.size, true);
      sidePanel.setVisible(side.shown !== undefined ? side.shown : locked || selected !== null, false);

      if (view.tab && tabs.some(function (tab) { return viewOf(tab) === view.tab; })) setView(view.tab);
      viewParts.forEach(function (part) {
        try { part.apply(view[part.name]); } catch (e) { console.error(e); }
      });

      if (layoutChanged) {
        revealedId = selected;
        showLayout(); // draws the tree, with everything below in it
      } else if (shareChanged) {
        renderWidelyUsed();
        markWidelyUsed();
      }
      updateChoices();
      markLeftOut();
      updateHideButtons();
      renderHidden();
      markHidden();
      updateHideAll();
      updateFilter();
      applyFilter();
      updateNotes();
      reveal(selected);
      emit("applied", selected);
      emit("view", activeView);
    }

    function openView(view) {
      applyView(view);
      currentView = view.name;
      currentArrangement = arrangementOf(captureView());
      var missing = missingFrom(view);
      if (missing.length > 0) {
        console.warn(tool + ": the view \"" + view.name + "\" mentions " + itemsText(missing.length) + " that this workbook doesn't have: " + missing.join(", "));
        showNotice("The view “" + view.name + "” mentions " + itemsText(missing.length) + " that this workbook doesn't have, so " +
          (missing.length === 1 ? "it's" : "they're") + " left out: " + listOf(missing) + ".");
      } else {
        hideNotice();
      }
      updateViewsSummary();
    }

    function showNotice(text) {
      $("page-notice-text").textContent = text;
      noticeBox.hidden = false;
    }

    function hideNotice() { noticeBox.hidden = true; }

    $("page-notice-close").addEventListener("click", hideNotice);

    function indexOfView(name, except) {
      var wanted = name.toLowerCase();
      for (var i = 0; i < savedViews.length; i++) {
        if (savedViews[i] !== except && savedViews[i].name.toLowerCase() === wanted) return i;
      }
      return -1;
    }

    function updateViewsSummary() {
      $("views-current").textContent = currentView === null ? "" : " · " + currentView;
      viewsSummary.title = currentView === null
        ? "Saved views: arrangements of the page to come back to, and to share as a file"
        : "Saved views. Showing “" + currentView + "”" + (renderedChanged ? ", changed since" : "");
    }

    var renderedChanged = false;

    // The list, and the buttons that depend on it. A view is "changed" once the page is arranged differently from how
    // it was when the view was opened or saved.
    function renderViewsMenu() {
      renderedChanged = currentView !== null && arrangementOf(captureView()) !== currentArrangement;
      viewsList.innerHTML = savedViews.map(function (view, i) {
        var current = view.name === currentView;
        // The view showing has Save changes instead, below.
        var saveOver = current ? "" : "<button type=\"button\" class=\"link-button\" data-view-save=\"" + i +
          "\" title=\"Save how the page is arranged now into this view, in place of what it holds, keeping its name and description\">Save over</button> ";
        return "<li class=\"views-item" + (current ? " current" : "") + "\">" +
          "<button type=\"button\" class=\"views-open\" data-view-open=\"" + i + "\"" + (current ? " aria-current=\"true\"" : "") +
          " title=\"Show the page as this view has it\"><span class=\"views-name\">" + escapeHtml(view.name) + "</span>" +
          (current && renderedChanged ? " <span class=\"views-changed\">changed</span>" : "") +
          (view.description ? "<span class=\"views-description\">" + escapeHtml(view.description) + "</span>" : "") + "</button>" +
          "<span class=\"views-item-actions\">" + saveOver + "<button type=\"button\" class=\"link-button\" data-view-edit=\"" + i +
          "\" title=\"Rename it or change its description\">Edit</button> <button type=\"button\" class=\"link-button\" data-view-delete=\"" + i +
          "\" title=\"Delete this view\">Delete</button></span></li>";
      }).join("");
      $("views-none").hidden = savedViews.length > 0;
      var update = $("views-update");
      var at = currentView === null ? -1 : indexOfView(currentView);
      update.hidden = at < 0 || !renderedChanged;
      update.textContent = at < 0 ? "" : "Save changes to “" + savedViews[at].name + "”";
      update.title = "Save how the page is arranged now into this view, keeping its name and description";
      var exportButton = $("views-export");
      exportButton.disabled = savedViews.length === 0;
      exportButton.title = savedViews.length === 0 ? "There are no views to export yet" : "Download the views as " + tool + "-views.json";
      updateViewsSummary();
    }

    // status: text, with an Undo button after it when undo is given (from listBefore). Undo is offered only until the
    // next status, so nothing else has changed the list in between.
    function setViewsStatus(text, isWarning, undo) {
      undoState = undo || null;
      viewsStatus.textContent = text;
      viewsStatus.classList.toggle("warning", !!isWarning);
      if (undo) {
        viewsStatus.appendChild(document.createTextNode(" "));
        var button = document.createElement("button");
        button.type = "button";
        button.className = "link-button";
        button.id = "views-undo";
        button.textContent = "Undo";
        viewsStatus.appendChild(button);
      }
    }

    // Keeps the views in the browser, and returns what to add to the status if it couldn't.
    function storeViewsAndSay() { return storeViews() ? "" : " This browser couldn't keep them, so export them to keep them."; }

    // The list as it is now, to put back with Undo, and what to say when it is.
    function listBefore(undone) {
      return { views: savedViews.slice(), current: currentView, arrangement: currentArrangement, undone: undone };
    }

    // Saves the page as it is now into the view at `at`, keeping its name, and its description unless given one. It's
    // then the view showing, as a new one would be.
    function saveOver(at, statusText, description) {
      var before = listBefore("Put “" + savedViews[at].name + "” back as it was.");
      var view = captureView();
      view.name = savedViews[at].name;
      view.description = description !== undefined ? description : savedViews[at].description;
      savedViews[at] = view;
      currentView = view.name;
      currentArrangement = arrangementOf(view);
      closeForm();
      renderViewsMenu();
      setViewsStatus(statusText + storeViewsAndSay(), false, before);
      $("views-undo").focus();
    }

    function openForm(view) {
      editing = view || "new";
      viewsForm.hidden = false;
      $("views-actions").hidden = true;
      viewsName.value = view ? view.name : "";
      viewsDescription.value = view ? view.description : "";
      viewsError.hidden = true;
      $("views-form-save").textContent = view ? "Save" : "Save view";
      viewsName.focus();
    }

    function closeForm() {
      editing = null;
      viewsForm.hidden = true;
      $("views-actions").hidden = false;
    }

    viewsForm.addEventListener("submit", function (event) {
      event.preventDefault();
      var name = viewsName.value.trim().slice(0, VIEW_NAME_LENGTH);
      var other = editing === "new" ? null : editing;
      viewsError.hidden = false;
      if (name === "") { viewsError.textContent = "Give the view a name."; viewsName.focus(); return; }
      if (indexOfView(name, other) >= 0) {
        viewsError.textContent = "There's already a view called “" + name + "”.";
        // A new view can go in its place instead.
        if (editing === "new") {
          viewsError.insertAdjacentHTML("beforeend", " <button type=\"button\" class=\"link-button\" id=\"views-save-over\" " +
            "title=\"Save the page as it is now into that view, keeping its name\">Save over it</button>");
        }
        viewsName.focus();
        return;
      }
      viewsError.hidden = true;
      if (editing === "new") {
        var view = captureView();
        view.name = name;
        view.description = viewsDescription.value.trim();
        savedViews.push(view);
        currentView = name;
        currentArrangement = arrangementOf(view);
      } else {
        if (currentView === editing.name) currentView = name;
        editing.name = name;
        editing.description = viewsDescription.value.trim();
      }
      closeForm();
      renderViewsMenu();
      setViewsStatus("Saved “" + name + "”." + storeViewsAndSay(), false);
      $("views-new").focus();
    });

    $("views-form-cancel").addEventListener("click", function () {
      closeForm();
      $("views-new").focus();
    });

    // Save over it, from the form's note that the name is taken: the description typed, if any, replaces the view's.
    viewsError.addEventListener("click", function (event) {
      if (!event.target.closest("#views-save-over")) return;
      var at = indexOfView(viewsName.value.trim().slice(0, VIEW_NAME_LENGTH));
      if (at < 0) return;
      var description = viewsDescription.value.trim();
      saveOver(at, "Saved over “" + savedViews[at].name + "”.", description === "" ? undefined : description);
    });

    $("views-new").addEventListener("click", function () { openForm(null); });

    $("views-update").addEventListener("click", function () {
      var at = indexOfView(currentView);
      if (at >= 0) saveOver(at, "Saved the changes to “" + savedViews[at].name + "”.");
    });

    viewsList.addEventListener("click", function (event) {
      var open = event.target.closest("[data-view-open]");
      var save = event.target.closest("[data-view-save]");
      var edit = event.target.closest("[data-view-edit]");
      var remove = event.target.closest("[data-view-delete]");
      if (open) {
        var view = savedViews[Number(open.getAttribute("data-view-open"))];
        viewsBox.open = false;
        setViewsStatus("");
        if (view) openView(view);
      } else if (save) {
        var target = Number(save.getAttribute("data-view-save"));
        if (savedViews[target]) saveOver(target, "Saved over “" + savedViews[target].name + "”.");
      } else if (edit) {
        openForm(savedViews[Number(edit.getAttribute("data-view-edit"))]);
      } else if (remove) {
        var at = Number(remove.getAttribute("data-view-delete"));
        var name = savedViews[at].name;
        var before = listBefore("Brought back “" + name + "”.");
        savedViews.splice(at, 1);
        if (currentView === name) currentView = null;
        closeForm();
        renderViewsMenu();
        setViewsStatus("Deleted “" + name + "”." + storeViewsAndSay(), false, before);
        $("views-undo").focus();
      }
    });

    viewsStatus.addEventListener("click", function (event) {
      if (!event.target.closest("#views-undo") || !undoState) return;
      var undo = undoState;
      savedViews = undo.views;
      currentView = undo.current;
      currentArrangement = undo.arrangement;
      renderViewsMenu();
      setViewsStatus(undo.undone + storeViewsAndSay(), false);
      $("views-new").focus();
    });

    // The menu is filled as it opens, and anything half-done in it is dropped when it closes.
    viewsSummary.addEventListener("click", function () {
      if (viewsBox.open) return;
      renderViewsMenu();
    });
    viewsBox.addEventListener("toggle", function () {
      if (viewsBox.open) return;
      closeForm();
      setViewsStatus("");
    });

    // ---- Export and Import ----

    function exportViews() {
      var text = JSON.stringify(sortedKeys({ formatVersion: VIEWS_FORMAT, tool: tool, views: savedViews }), null, 2) + "\n";
      var link = document.createElement("a");
      link.href = URL.createObjectURL(new Blob([text], { type: "application/json" }));
      link.download = tool + "-views.json";
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.setTimeout(function () { URL.revokeObjectURL(link.href); }, 60000);
      setViewsStatus("Exported " + viewsText(savedViews.length) + " as " + tool + "-views.json.", false);
    }

    $("views-export").addEventListener("click", function () { if (savedViews.length > 0) exportViews(); });
    $("views-import-button").addEventListener("click", function () { viewsInput.click(); });

    viewsInput.addEventListener("change", function () {
      var file = viewsInput.files && viewsInput.files[0];
      if (!file) return;
      var reading = file.text ? file.text() : new Response(file).text();
      reading.then(function (text) { importViews(text, file.name); }, function () {
        setViewsStatus("Couldn't read " + file.name + ".", true);
      }).then(function () { viewsInput.value = ""; }); // so the same file can be read again
    });

    // Views read from a file are added, replacing any of the same name, and nothing else changes until one is opened.
    function importViews(text, fileName) {
      var file;
      try { file = JSON.parse(text); } catch (e) { file = null; }
      if (!file || typeof file !== "object" || !Array.isArray(file.views) || typeof file.formatVersion !== "number" || file.formatVersion < 1) {
        setViewsStatus(fileName + " isn't a views file.", true);
        return;
      }
      if (file.tool !== tool) {
        setViewsStatus(fileName + " holds " + (typeof file.tool === "string" ? file.tool + "'s" : "another tool's") + " views, not " + tool + "'s.", true);
        return;
      }
      if (file.formatVersion > VIEWS_FORMAT) {
        setViewsStatus(fileName + " was written by a newer " + tool + " than the one that made this page. Run " + tool + " again to read it.", true);
        return;
      }
      var views = file.views.map(cleanView).filter(function (view) { return view !== null; });
      var skipped = file.views.length - views.length;
      var replaced = 0;
      var missing = new Set();
      views.forEach(function (view) {
        var at = indexOfView(view.name);
        if (at >= 0) { savedViews[at] = view; replaced++; } else savedViews.push(view);
        var notHere = missingFrom(view);
        notHere.forEach(function (id) { missing.add(id); });
        if (notHere.length > 0) {
          console.warn(tool + ": the view \"" + view.name + "\" in " + fileName + " mentions " + itemsText(notHere.length) + " that this workbook doesn't have: " + notHere.join(", "));
        }
      });
      renderViewsMenu();
      var ids = Array.from(missing).sort();
      setViewsStatus("Imported " + viewsText(views.length) + " from " + fileName +
        (replaced > 0 ? ", replacing " + replaced + " of the same name" : "") + "." +
        (skipped > 0 ? " " + skipped + " without a name " + (skipped === 1 ? "was" : "were") + " left out." : "") +
        (ids.length > 0 ? " " + (views.length === 1 ? "It mentions " : "They mention ") + itemsText(ids.length) +
          " that this workbook doesn't have, which will be left out: " + listOf(ids) + "." : "") + storeViewsAndSay(), ids.length > 0);
    }

    function addViewPart(name, part) {
      if (SHELL_PARTS.indexOf(name) >= 0) throw new Error("A view part can't be called " + name);
      viewParts.push({ name: name, save: part.save, apply: part.apply, ids: part.ids });
    }

    // ---- Tabs ----

    var tabs = Array.prototype.slice.call(document.querySelectorAll(".tabs [role=\"tab\"]"));
    function viewOf(tab) { return tab.id.slice(4); } // "tab-graph" -> "graph"
    var activeView = viewOf(tabs[0]);

    // Shows a tab's view without telling the tool, for a saved view, which tells it once everything's set.
    function setView(name, focus) {
      activeView = name;
      tabs.forEach(function (tab) {
        var on = viewOf(tab) === name;
        tab.setAttribute("aria-selected", on ? "true" : "false");
        tab.tabIndex = on ? 0 : -1;
        $(tab.getAttribute("aria-controls")).hidden = !on;
        if (on && focus) tab.focus();
      });
      if (options.rememberView) settings.save("view", name);
    }

    function showView(name, focus) {
      setView(name, focus);
      emit("view", name);
    }

    tabs.forEach(function (tab, index) {
      tab.addEventListener("click", function () { showView(viewOf(tab)); });
      tab.addEventListener("keydown", function (e) {
        if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
        var next = tabs[(index + (e.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length];
        showView(viewOf(next), true);
        e.preventDefault();
      });
    });

    function start() {
      var stored = options.rememberView ? settings.load("view") : null;
      var known = tabs.some(function (tab) { return viewOf(tab) === stored; });
      showView(known ? stored : viewOf(tabs[0]));
    }

    return {
      data: data,
      escapeHtml: escapeHtml,
      settings: settings,
      theme: currentTheme,
      onThemeChange: function (fn) { listeners.theme.push(fn); },
      activeView: function () { return activeView; },
      showView: showView,
      onViewChange: function (fn) { listeners.view.push(fn); },
      start: start,
      panel: {
        element: panel, header: $("side-panel-header"), body: $("side-panel-body"),
        following: function () { return following; }
      },
      selectionChanged: selectionChanged,
      focused: function () { return focused; },
      onFocusChange: function (fn) { listeners.focus.push(fn); },
      focusOnClick: function () { return focusOnClick; },
      setFocusOnClick: setFocusOnClick,
      explorer: { setLayouts: setLayouts },
      items: { init: initItems },
      filter: {
        active: filterActive,
        has: function (id) { return ticked.has(id); },
        ids: filterIds,
        clear: function () { setTicked([]); }
      },
      onFilterChange: function (fn) { listeners.filter.push(fn); },
      favourites: {
        list: favouriteList,
        has: isFavourite,
        toggle: toggleFavourite,
        buttonHtml: function (id) { return starHtml(id, "panel-star"); }
      },
      onFavouritesChange: function (fn) { listeners.favourites.push(fn); },
      focusFavourites: focusFavourites,
      setFocusFavourites: setFocusFavourites,
      hidden: {
        list: hiddenList,
        has: isHidden,
        toggle: toggleHidden,
        clear: function () { setHidden([]); },
        buttonHtml: function (id) { return hideHtml(id, "panel-hide"); },
        noteHtml: noteHtml
      },
      onHiddenChange: function (fn) { listeners.hidden.push(fn); },
      choices: {
        init: initChoices,
        on: choiceOn,
        value: choiceValue,
        count: choicesCount,
        key: choicesKey,
        reset: resetChoices
      },
      onChoicesChange: function (fn) { listeners.choices.push(fn); },
      views: { part: addViewPart },
      onViewApplied: function (fn) { listeners.applied.push(fn); },
      source: {
        init: initSource,
        has: function (id) { return sources.has(id); },
        lines: function (id) { return sources.get(id); },
        codeHtml: codeHtml,
        showLine: showLine
      },
      references: {
        listHtml: referenceListHtml
      }
    };
  }

  window.ExcelDocumenterShell = { create: create };
})();
