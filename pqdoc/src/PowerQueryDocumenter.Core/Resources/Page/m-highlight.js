// m-highlight.js: a small Power Query M highlighter for the details panel's source view, parallel to vbadoc's
// vba-highlight.js and using the same token classes. It marks keywords (including #date, #table and the other
// # constructors), text, comments, numbers, quoted names (#"Sales Data"), and type names: the primitive ones
// (text, number, nullable, ...) and dotted ones ending in .Type, such as Int64.Type. Text, quoted names and block
// comments can run over several lines, so the state carries from one line to the next.
//
// It scans each line once, and finds the end of text with indexOf, so an Enter Data query's very long line of
// compressed data stays quick.
//
// Usage: MHighlight.highlightLines(arrayOfLines) returns one HTML string per line.
(function () {
  "use strict";

  var KEYWORDS = new Set((
    "and as catch each else error false if in is let meta not null or otherwise section shared then true try type"
  ).split(" "));

  var HASH_KEYWORDS = new Set((
    "#binary #date #datetime #datetimezone #duration #infinity #nan #sections #shared #table #time"
  ).split(" "));

  var TYPES = new Set((
    "any anynonnull binary date datetime datetimezone duration function list logical none number record table text " +
    "time nullable optional"
  ).split(" "));

  // What a multi-line token is called in the state, and the class it's drawn with.
  var KINDS = { text: "string", name: "name", comment: "comment" };

  function escapeHtml(text) {
    return text.replace(/[&<>]/g, function (c) { return c === "&" ? "&amp;" : c === "<" ? "&lt;" : "&gt;"; });
  }

  function token(kind, text) {
    return "<span class=\"tok-" + kind + "\">" + escapeHtml(text) + "</span>";
  }

  function isDigit(c) { return c >= "0" && c <= "9"; }

  function isIdentifierStart(c) { return (c >= "A" && c <= "Z") || (c >= "a" && c <= "z") || c === "_" || c > "¿"; }

  function isIdentifierPart(c) { return isIdentifierStart(c) || isDigit(c); }

  // The index just after the quote that ends text starting at `from`, or -1 if it runs on. "" is an escaped quote.
  function quoteEnd(line, from) {
    var i = from;
    for (;;) {
      var quote = line.indexOf("\"", i);
      if (quote < 0) return -1;
      if (line[quote + 1] === "\"") { i = quote + 2; continue; }
      return quote + 1;
    }
  }

  // The index just after "*/", or -1 if the comment runs on.
  function blockEnd(line, from) {
    var close = line.indexOf("*/", from);
    return close < 0 ? -1 : close + 2;
  }

  // A (possibly dotted) identifier such as Table.SelectRows or Int64.Type; ".." is the range operator, not a dot.
  function identifierEnd(line, i) {
    for (;;) {
      while (i < line.length && isIdentifierPart(line[i])) i++;
      if (line[i] === "." && i + 1 < line.length && isIdentifierStart(line[i + 1])) { i++; continue; }
      return i;
    }
  }

  function numberEnd(line, i) {
    if (line[i] === "0" && (line[i + 1] === "x" || line[i + 1] === "X")) {
      i += 2;
      while (i < line.length && /[0-9A-Fa-f]/.test(line[i])) i++;
      return i;
    }
    while (i < line.length && isDigit(line[i])) i++;
    if (line[i] === "." && isDigit(line[i + 1] || "")) {
      i++;
      while (i < line.length && isDigit(line[i])) i++;
    }
    if (line[i] === "e" || line[i] === "E") {
      var exponent = line[i + 1] === "+" || line[i + 1] === "-" ? i + 2 : i + 1;
      if (isDigit(line[exponent] || "")) {
        i = exponent;
        while (i < line.length && isDigit(line[i])) i++;
      }
    }
    return i;
  }

  // Highlights one line. `state` is the multi-line token the line starts inside ("text", "name" or "comment"), or
  // null; the result says which one the next line starts inside.
  function highlightLine(line, state) {
    var html = "";
    var i = 0;

    if (state) {
      var close = state === "comment" ? blockEnd(line, 0) : quoteEnd(line, 0);
      if (close < 0) return { html: token(KINDS[state], line), state: state };
      html += token(KINDS[state], line.slice(0, close));
      i = close;
    }

    while (i < line.length) {
      var c = line[i];
      var next = line[i + 1] || "";
      var end;

      if (c === "/" && next === "/") {
        return { html: html + token("comment", line.slice(i)), state: null };
      }
      if (c === "/" && next === "*") {
        end = blockEnd(line, i + 2);
        if (end < 0) return { html: html + token("comment", line.slice(i)), state: "comment" };
        html += token("comment", line.slice(i, end));
        i = end;
        continue;
      }
      if (c === "\"") {
        end = quoteEnd(line, i + 1);
        if (end < 0) return { html: html + token("string", line.slice(i)), state: "text" };
        html += token("string", line.slice(i, end));
        i = end;
        continue;
      }
      if (c === "#" && next === "\"") {
        end = quoteEnd(line, i + 2);
        if (end < 0) return { html: html + token("name", line.slice(i)), state: "name" };
        html += token("name", line.slice(i, end));
        i = end;
        continue;
      }
      if (c === "#" && isIdentifierStart(next)) {
        end = i + 1;
        while (end < line.length && isIdentifierPart(line[end])) end++;
        var hashWord = line.slice(i, end);
        html += HASH_KEYWORDS.has(hashWord) ? token("keyword", hashWord) : escapeHtml(hashWord);
        i = end;
        continue;
      }
      if (isIdentifierStart(c)) {
        end = identifierEnd(line, i);
        var word = line.slice(i, end);
        html += KEYWORDS.has(word) ? token("keyword", word)
          : TYPES.has(word) || /\.Type$/.test(word) ? token("type", word)
          : escapeHtml(word);
        i = end;
        continue;
      }
      // The range operator, so the 3 in {1..3} isn't read as .3.
      if (c === "." && next === ".") {
        html += "..";
        i += 2;
        continue;
      }
      if (isDigit(c) || (c === "." && isDigit(next))) {
        end = numberEnd(line, i);
        html += token("number", line.slice(i, end));
        i = end;
        continue;
      }
      html += escapeHtml(c);
      i++;
    }

    return { html: html, state: null };
  }

  function highlightLines(lines) {
    var result = [];
    var state = null;
    for (var n = 0; n < lines.length; n++) {
      var highlighted = highlightLine(lines[n], state);
      result.push(highlighted.html);
      state = highlighted.state;
    }
    return result;
  }

  window.MHighlight = { highlightLines: highlightLines };
})();
