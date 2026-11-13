# Generalisation plan: Access, C# and Python

This plan widens the repository from Excel workbooks to code in general. Three new tools document a Microsoft Access
database (its forms and VBA), a C# solution (its projects, types and members) and a Python project of many files,
with the page vbadoc makes: an explorer, a dependency graph, a class diagram, the Calls tab and saved views. The
Access tool also shows how forms and subforms are put together, so they can be rebuilt as Blazor components. The
repository, the shared library and the desktop app get names that fit the wider goal; vbadoc and pqdoc keep theirs.

Before any of that, it finishes what the feedback plan parked and adds two things asked for on 2026-11-13: locking a
view so that clicking around doesn't change it, and peeking at a reference without leaving the selection.

It is written to be handed to a fresh Claude Code session (or a developer) to carry out.

**Status: agreed on 2026-11-13.** Your answers are in section 1, and the rest of the plan was revised for them in
step K0. The progress table says where the work is.

## How to use this plan

- Read the whole file before starting. Sections 1–5 are the background and the decisions; section 6 has the steps.
- Work **one step at a time**, each on a branch of its own (`general/<step>`, such as `general/l1-lock-view`), with a
  commit per change. At the end of each step:
  1. `dotnet build <root solution> -warnaserror` and the tests of everything touched pass;
  2. update the progress table below and `docs/WORKLOG.md`;
  3. raise the version of each tool the step changed;
  4. commit;
  5. summarise what changed and how to check it, then **stop and wait for review**;
  6. you rebase onto main and push after the review.
- Ask before doing anything that goes against this plan, a tool's `Requirements.md` or `DECISIONS.md`, or the
  guidelines. Section 7 lists the changes to those that this plan needs.
- Use Australian English in everything we write.
- **The tools are in alpha** (5.1): a step may change what a page or an output file looks like, so steps are checked
  by their tests and by looking at screenshots, not by comparing outputs byte for byte with the version before.

## Progress

| Step | Scope | Who | Status |
|---|---|---|---|
| [K0](#step-k0-settle-the-questions) | Settle the questions in section 1; close the feedback plan | You, then Claude | Done |
| [K1](#step-k1-names-inside-the-repository) | Names inside the repository: the root solution, the shared library, the shell's global, the publish folder, the documents | Claude | Not started |
| [K2](#step-k2-the-desktop-apps-name) | The desktop app becomes `codedocui` | Claude, with VS Code closed | Not started |
| [K3](#step-k3-the-repositorys-name) | The repository's name on GitHub and on disk, `PATH`, and Claude's memory folder | You, then Claude | Not started |
| [L1](#step-l1-lock-the-view) | Lock view: what's shown stays as it is while you click around, and only the source changes | Claude | Not started |
| [L2](#step-l2-peek-at-a-reference) | Peek: a line in Uses / Used by opens a preview of the code around it, without leaving the selection | Claude | Not started |
| [L3](#step-l3-calls-tab-follow-ups-e5b) | Calls tab follow-ups (E5b): Ifs whose conditions make calls, and the sequence diagram's participants staying in view | Claude | Not started |
| [L4](#step-l4-views-file-built-into-the-page-g2) | A views file in the output folder is built into the page (G2) | Claude | Not started |
| [L5](#step-l5-printing-and-descriptions-g3) | Printing a view or every view, with descriptions (G3) | Claude, then you on a work machine | Not started |
| [L6](#step-l6-copy-a-view-as-text-g4) | Copy a view as text, to paste into a chat (G4) | Claude | Not started |
| [M1](#step-m1-the-shared-library-in-three) | The shared library in three: what every tool needs, what only workbooks need, and the code views. The shell says "source", not "workbook" | Claude | Not started |
| [M2](#step-m2-vbadocs-views-move-to-the-code-views-library) | vbadoc's graph, class diagram and Calls tab move to the code views library | Claude | Not started |
| [M3](#step-m3-the-code-model) | The code model: the views draw what a language describes, and files are apart from the things in them. vbadoc is its first language | Claude | Not started |
| [N1](#step-n1-sample-databases-and-a-spike-on-reading-them) | Sample Access databases, and a spike on getting forms and VBA out of them | Claude, then you at home and on a work machine | Not started |
| [N2](#step-n2-accessdoc-reads-a-databases-vba) | `accessdoc`: the VBA language in a library vbadoc and accessdoc share; the tool's skeleton; a page of the database's VBA | Claude | Not started |
| [N3](#step-n3-accessdoc-reads-forms) | `accessdoc`: forms read into sections, controls, subforms, data sources and events, with handlers linked to their controls | Claude | Not started |
| [N4](#step-n4-the-forms-view) | `accessdoc`: the Forms view: which form holds which, each form's layout drawn, its controls, data and events | Claude, then you on a real database | Not started |
| [N5](#step-n5-a-forms-outline-for-rebuilding-it) | `accessdoc`: a form's outline for rebuilding it as components, copied as text | Claude, then you | Not started |
| [P1](#step-p1-spike-on-reading-a-c-solution) | Spike: read a C# solution with Roslyn and no build, on this repository and a real solution of yours | Claude, then you on a work machine | Not started |
| [P2](#step-p2-csdoc-reads-a-solution) | `csdoc`: the tool's skeleton, reading a solution into projects, namespaces, types and members | Claude | Not started |
| [P3](#step-p3-csdocs-dependencies) | `csdoc`: references with their lines, inheritance, interfaces, events, entry points and what's unreferenced | Claude | Not started |
| [P4](#step-p4-csdocs-page) | `csdoc`: the page, with the explorer, graph, class diagram, source with a C# highlighter, and doc comments | Claude, then you on a real solution | Not started |
| [P5](#step-p5-csdocs-calls) | `csdoc`: the Calls tab and sequence diagram | Claude | Not started |
| [P6](#step-p6-the-graph-at-group-level) | The graph at group level (projects, namespaces, packages, folders), in the code views, with a check on a larger solution | Claude | Not started |
| [P7](#step-p7-older-net-framework-projects) | `csdoc`: older .NET Framework project files | Claude | Not started |
| [Q1](#step-q1-your-colleagues-project-a-sample-and-a-spike) | Python: what to ask your colleague, a sample project, and a spike on reading Python without running it | You and your colleague, then Claude | Not started |
| [Q2](#step-q2-pythondoc-reads-a-project) | `pythondoc`: the tool's skeleton, reading a project into packages, modules, classes and functions | Claude | Not started |
| [Q3](#step-q3-pythondocs-dependencies) | `pythondoc`: imports, references with their lines, inheritance, and what couldn't be followed | Claude | Not started |
| [Q4](#step-q4-pythondocs-page-and-calls) | `pythondoc`: the page, a Python highlighter, docstrings, and the Calls tab | Claude, then your colleague | Not started |
| [R1](#step-r1-the-desktop-app-takes-everything) | The desktop app documents databases, solutions and Python projects | Claude | Not started |
| [R2](#step-r2-finishing) | Finishing: the guidelines, the READMEs, and closing this plan | Claude | Not started |

## 1. Your answers

**Answered on 2026-11-13.** The tables below are kept as you left them. Where an answer said something other than
the recommendation, K0 revised the plan:

| Your answer | What changed |
|---|---|
| Look at the Access [Version Control add-in](https://github.com/joyfullservice/msaccess-vcs-addin), and use what you can without assuming it can be installed | [5.5](#55-reading-access): it's VBA that runs inside Access, built on the same `SaveAsText` the recommendation uses, so it confirms that way in and doesn't replace it. accessdoc reads a folder the add-in exported as well as its own, takes the add-in's list of what to leave out of form text so git shows only real changes, and N1's fixtures start from the add-in's own published test source |
| `.accdb` only, no password, not split | `.mdb`, compiled and password-protected databases are left to [Later](#9-later); accessdoc says plainly that it can't read them |
| Local tables, tables linked to SQL databases, and temporary tables that come and go | [5.5](#55-reading-access): tables are listed as local or linked, a linked table's connection is shown without its password, and a table named in code or a query but not in the database is "not in the database when it was read", never an error. Table definitions aren't written to the source folder, so a left-over temporary table doesn't show up in git |
| About 150 forms, nested perhaps tens deep | [4.4](#44-the-forms-view-access), N4: the Forms view opens on a tree, draws one form and what's inside it at a time, and never draws every form at once |
| Writing Blazor code would be very helpful; later, but allow for it now | N3 and N5: the outline is written from a forms model that holds everything a generator would need (each control's kind, place, size, binding and events, in Access's own units), so writing component skeletons is a later step over the same model. It leads [Later](#9-later) |
| A public sample, such as Northwind, in place of one you make | N1: you don't make one. The committed fixtures are the add-in's test source, and a small database Claude builds from text by script. Northwind 2.0 is made from Access's own template into `private/` as the larger test; I found nothing that lets it be redistributed, so it isn't committed |
| Older .NET Framework projects are lower priority | They move out of P1 and P2 into a step of their own at the end, P7 |
| Your colleague is new to Python too, and inherited the code | [5.7](#57-reading-python), Q1, Q4: the spike answers the technical questions from the project itself, the questions for your colleague are ones a newcomer can answer, and the page and README are written for someone finding their way around code they didn't write |
| Their machine may be more locked down than yours | Q1, Q4: you run pythondoc for them, and they only open the page |

### 1.1 Settled already

| | Settled on 2026-11-13 |
|---|---|
| Names | The table in [4.1](#41-names), as proposed |
| The feedback plan | Closed and deleted. Its parked steps (G2–G4, E5b) are steps L3–L6 here. Your outstanding checks are closed; the list of them is in the history at the tag `feedback-plan-final`, and E5b's original notes at `feedback-plan-e5b-notes` |
| Keeping outputs the same from one version to the next | Not needed while the tools are in alpha (5.1) |
| The feedback notes file | Deleted |

### 1.2 The order of the work

| Question | Recommended | Your answer |
|---|---|---|
| Names (K) or page follow-ups (L) first? | Names first: K1 and K2 are a day's mechanical work, and L then lands under the new names. L doesn't depend on them, so it can go first if you'd rather have Lock view and Peek sooner | Names first |
| Which new tool first? | Access, then C#, then Python. Access reuses vbadoc's VBA reading whole, so it's the shortest road to something you can use, and it's the one your Blazor work is waiting on. Python last: it's for someone else and has the most guesswork | Access, C#, Python |
| Is L6 (copy a view as text) wanted? It was optional in the feedback plan | Yes: N5's outline for Blazor is the same kind of thing, so it's built once | Yes |

### 1.3 Lock view (step L1)

How I've read what you asked for: a button that keeps what the graph and class diagram show exactly as it is. While
it's on, a click on an item, or on empty space, changes only the selection and the source panel. Tell me if any of
this is wrong.

| Question | Recommended | Your answer |
|---|---|---|
| What the button is called | **Lock view**, a padlock at the start of each view's toolbar, beside Focus | Lock view |
| What it keeps | The items shown, where they are, the zoom, and the level (modules or members). Nothing is added, removed or moved by a click | As recommended |
| How the selected item is shown while locked | Outlined, so you can see what the source panel is showing. Nothing else is dimmed or lit | Outlined only |
| Where the locked items show | Ticked in the explorer, as you described: locking ticks everything shown, so the explorer says what's kept, and you can untick one to drop it or tick another to add it | Ticked in the explorer |
| Unlocking | The button again. The ticks go back to what they were before locking, and Focus on click works again | As recommended |
| One lock for the page, or one per view | One for the page: the graph and the class diagram already show the same items | One for the page |
| Dragging a node while locked | Still allowed. It moves that node only | Allowed |
| Is it kept in a saved view? | Yes | Yes |
| pqdoc's graph too? | Yes, it's the shell's | Yes |

### 1.4 Peek (step L2)

| Question | Recommended | Your answer |
|---|---|---|
| What a click on a line in Uses / Used by does | Opens a preview under that line: the code around it, highlighted, with the line marked. The selection, the views and the source below stay as they are | Peek |
| How to go there instead, as a click does now | A **Go to** button in the preview, and Ctrl+click on the line (Ctrl+click already means "the other thing" in every view) | Go to, and Ctrl+click |
| How much code the preview shows | The line and 5 lines either side, with a button to show the whole procedure or query | 5 lines either side |
| How many previews can be open | Any number. Each closes with a click on its line again; Esc closes the last opened | Any number |
| Both tools? | Yes, it's the shell's list | Yes |

### 1.5 Access

| Question | Recommended | Your answer |
|---|---|---|
| How accessdoc gets forms and VBA out of a database (see [5.5](#55-reading-access)) | Through Access itself: given a database, accessdoc copies it, has Access write every form and module as text from the copy, and reads the text. Given a folder of that text, it needs no Access. N1's spike tries this, and reading the file directly, before anything is built | Please take a look at this MS Access [Version Control Add-in](https://github.com/joyfullservice/msaccess-vcs-addin) which seems have some crossover with what we need (let me know if you are unable to access it and I can provide a copy of it's source code/repo). I have used it in the past, however recent Group Policy changes on my work environment means that its not as straight forward to use. Use what you can from it, but don't assume I can install or run it myself. If this does not help, then it sounds like it would be best to do as you recommended. |
| Is Access installed where you'll run it, at home and at work? Which version, and 32- or 64-bit if you know? | Needed for the answer above | 64-bit on both. The version that lets you modify files (not just the runtime) |
| Which files: `.accdb`, older `.mdb`, both? Are they split into a front end and a back end of tables? Are any compiled (`.accde`), or opened with a password? | A compiled file has no source to read, and a password would have to be typed into Access by you | Lets assume `.accdb` only, with no password. No split as far as I know. Some projects use a combination of local tables and connections to SQL databases. Additionally, some projects generate temporary local tables from the connected SQL tables during export procedures. These may or may not be present at any given point due to bugs in how it cleans up the temp tables, if a procedure fails, or is exited early. |
| Roughly how many forms does a database have, and how deep do subforms nest? | Sets how the Forms view is laid out | Roughly 150 forms. Not sure how deeply nested, could be 10s of forms deep. |
| Reports | Later: they're read the same way as forms, so adding them is small once forms work | Later |
| Queries and tables | Named wherever a form or control uses one, and a query's SQL shown. A table's fields listed if the spike finds them cheap to get. No diagram of table relationships | As recommended |
| Access macros (the kind built in the macro designer, embedded ones included) | Read far enough to follow them: an event that runs a macro links to the forms it opens and the VBA it runs | As recommended |
| What the outline for Blazor holds (N5) | For each form: the forms inside it and how each is linked to its parent, its data source, each control with what it's bound to and its size and place, and each event with the code it runs. As text you can paste into a chat. It doesn't write Blazor code | As recommended, though having it write blazor code would be very helpful. Perhaps we add this later, but provision for it now. |
| Can you make a small sample database in Access with made-up content, as you did the sample workbooks? | Needed for N1: the repository is public, so real databases stay in `private/` | Are there any publicly available ones? [Northwind](https://support.microsoft.com/en-us/access/northwind-2-0-developer-edition)? If not I can try, but I'd prefer not to. |

### 1.6 C#

| Question | Recommended | Your answer |
|---|---|---|
| How csdoc reads C# (see [5.6](#56-reading-c)) | Roslyn, with csdoc working the solution out for itself: nothing is built, restored or run | As recommended; P1 to confirm |
| Restored or not? | You weren't sure, and it needn't matter: csdoc works either way, and knows more about NuGet packages' types when a restore has been done. P1 reports the difference on your solutions | Works either way |
| Older .NET Framework projects | In scope, for reading other people's code. P2 has a fixture of one | In scope, but lower priority. |
| Size | Small to moderate, so no step of its own for large solutions. P6 checks one larger solution so there's a known limit | As recommended |
| Razor components (`.razor`), since you're building in Blazor | Later, as its own step once csdoc's page exists. Say if you'd rather have it in this plan | Later |
| Anything else that should be read: VB.NET, F#, XAML? | No | No |

### 1.7 Python

You said you have little experience of Python and the tool is for a colleague, so this plan doesn't ask you to judge
Python. [5.7](#57-reading-python) explains the words it uses, and Q1 has a short list to pass to your colleague.

| Question | Recommended | Your answer |
|---|---|---|
| How pythondoc reads Python | A parser of our own, so the tool needs nothing installed. While it's being built, its reading is checked against Python's own parser on this machine, across thousands of files, so its accuracy doesn't rest on anyone's eye | As recommended; Q1 to confirm |
| Who says whether the page is right and useful? | Your colleague, at Q4. Before that, the checks are automatic | My colleague, however they are also a python novice who's inherited code from someone else that's no longer available for support. |
| Can a copy of their project be used for checking, kept in a git-ignored `private/` folder and never committed? | Yes if they agree. If not, Q1's spike is given to them to run, and prints only counts | Yes, so long as it's git-ignored as you state. |
| Does their machine have the same limits as your work machine? | Assumed yes: one self-contained executable, nothing installed | Assume yes. Theirs may be more restricted than mine so I'd have to run this tool on their behalf. |

### 1.8 Smaller questions

| Question | Recommended | Your answer |
|---|---|---|
| Where a page goes by default | Beside the database or solution file, or in the Python project's root. `--output` changes it | As recommended |
| Is source in the page? | No, as now: Load folder reads it. `--embed-source` puts it in | As recommended |
| The Export tab and `import` for the new tools | No. Moving forms between Access databases is in Later | No |
| The top bar's summary, in place of "Workbook saved" | For a database, when it was last saved, as for a workbook. For a solution or project, counts only | As recommended |
| The desktop app's remembered settings when it's renamed | Start afresh: a few recent files and tick boxes | Start afresh |

## 2. Goals and non-goals

**Goals**

1. Lock a view and peek at a reference, and finish what the feedback plan parked: the Calls tab's follow-ups, views
   built into the page, printing, and copying a view as text.
2. Document an Access database: its VBA as vbadoc does a workbook's, and its forms, with which form holds which, each
   form's layout, controls, data and events, in enough detail to rebuild it as Blazor components.
3. Document a C# solution: its projects, namespaces, types and members, what uses what and on which lines, what
   inherits and implements what, and the path a call takes.
4. Document a Python project of many files the same way: packages, modules, classes, functions and methods.
5. One page design and one set of views across every language, so a feature built once reaches all of them.
6. Names that fit: the repository, the shared code and the desktop app stop saying Excel where they don't mean it.
7. The desktop app runs every tool.

**Non-goals**

- Building, restoring or running the code that's read. The one exception is asking Access to write a copy of a
  database out as text, which runs none of the database's own code (5.5).
- Writing Blazor code, or converting anything, in this plan. accessdoc describes a form, and rebuilding it is yours.
  The forms model is kept complete enough for a later step to write component skeletons from it (N3).
- API reference documentation, as DocFX and Sphinx make.
- Complete type inference for Python, or following reflection, dependency injection containers, `dynamic`, `getattr`
  or `eval`. The page says what couldn't be followed.
- Other languages, and links between languages. See [Later](#9-later).
- Changing source code or databases. There's no Export tab and no `import` for the new tools.
- Anything that needs the network, an install or admin rights.

## 3. What the tools are for, and their constraints

Carried over from the feedback plan, which is now closed, so that they have a home until R2 moves them to the
guidelines.

**Objectives**

1. **Understand code** that's new, or not worked on for a while. What matters most is the path execution takes for
   one activity, not everything at once.
2. **Leave documentation** for whoever works on it next.
3. New with this plan: **see how an Access form is built,** well enough to rebuild it elsewhere.

**Constraints**

- **Developers run the tools on locked-down work machines.** Application control limits where and by whom executables
  can run, nothing that needs an installer can be installed, and the only AI help is a chat window with no access to
  the project. Each new executable is tried on a work machine early (N1, P1, Q1).
- **Readers only open the page.** It works with nothing installed, no network and no special settings.
- **Everything works from the `dotnet` command line,** on .NET 10. Each tool publishes as one self-contained
  executable.
- **Pages load nothing from the network,** and never say when they were made.
- **Every dependency is compatible with the MIT licence.** Check the licence and the current version when adding one.
- **The repository is public, and real workbooks, databases and code aren't.** They stay in git-ignored `private/`
  folders; fixtures are made for the purpose, with made-up content, and anything Office writes into them about the
  person or machine that made them is removed before they're committed.

## 4. What it will look like

### 4.1 Names

The short name is the product and the long name is the code, as the guidelines have it.

| | Now | Becomes |
|---|---|---|
| GitHub repository and local folder | `ExcelDocumentationTools` | `CodeDocumentationTools` |
| Root solution | `ExcelDocumentationTools.sln` | `CodeDocumentationTools.sln` |
| Publish folder | `…\source\Executables\ExcelDocumentationTools\` | `…\source\Executables\CodeDocumentationTools\` |
| Shared library | `ExcelDocumenter.Shared` | `CodeDocumenter.Shared`, `CodeDocumenter.Workbooks`, `CodeDocumenter.CodeViews` and `CodeDocumenter.Vba` (5.3) |
| The shell's JavaScript global | `ExcelDocumenterShell` | `CodeDocumenterShell` |
| Desktop app | `xldocui`, `ExcelDocumenter.Desktop` | `codedocui`, `CodeDocumenter.Desktop` |
| Name kept for a combined tool | `xldoc` | `codedoc` |
| VBA in workbooks | `vbadoc`, `VbaDocumenter` | unchanged |
| Power Query | `pqdoc`, `PowerQueryDocumenter` | unchanged |
| Access | none | `accessdoc`, `AccessDocumenter`, in `accessdoc/`, writing `<database>-access.html` |
| C# | none | `csdoc`, `CSharpDocumenter`, in `csdoc/`, writing `<solution>-csharp.html` |
| Python | none | `pythondoc`, `PythonDocumenter`, in `pythondoc/`, writing `<project>-python.html` |

`pydoc` is avoided because Python ships a command of that name. `accessdoc` is new since you agreed the table; change
it here if you'd like another.

### 4.2 Commands

```
accessdoc <database, or a folder of its exported text>
csdoc     <solution, project or folder>
pythondoc <project folder>

each with:  [--output <folder>] [--output-name <name>] [--exclude <pattern>…] [--embed-source]
            [--on-exists …] [--force] [--verbose] [--quiet]
```

- `accessdoc` takes an `.accdb`, and writes the forms, modules, queries and macros as text into a folder beside the
  page, kept in step with the database as vbadoc's module files are, so they can go into git. Given that folder, or
  one the Access Version Control add-in exported (`<database>.src`), it reads it without Access.
- `csdoc` takes a `.sln`, `.slnx` or `.csproj`, or a folder holding exactly one solution.
- `pythondoc` takes the project's root folder, leaving out virtual environments, `__pycache__`, build folders and
  folders whose names start with a dot.

### 4.3 How each language fills the page

| | VBA in a workbook (now) | Access | C# | Python |
|---|---|---|---|---|
| Source | A workbook or add-in | A database | A solution | A project folder |
| Groups, shown as folders | `'@Folder` | Forms, reports, modules; `'@Folder` | Projects, then namespaces | Packages (folders) |
| Units, the graph's upper level | Modules: standard, class, interface, document, form | The same, with a form's module tied to its form | Types: class, interface, struct, record, enum, delegate | Modules and classes |
| Members | Procedures, properties, variables, constants, enums, types, events, declares | The same | Methods, constructors, properties, fields, events, indexers, operators, enum members | Functions, methods, properties, variables |
| Relationships | Uses, Implements, Events | The same, and Opens (a form opening another), Holds (a subform) | Uses, Inherits, Implements, Events | Uses, Inherits, Imports |
| Called without a reference in the code | Macros, worksheet functions, event handlers | Event properties, macros, the start-up form | `Main`, public members of a library, overrides, tests, members with attributes | `__main__` blocks, dunder methods, decorated functions, tests |
| Can't be followed | Late-bound calls | The same, and form references built from text | `dynamic`, reflection, unresolved names | Attribute calls on values of unknown type |
| Explorer layouts | Folders, Types | Forms, Folders, Types | Projects, Namespaces, Types | Packages, Types |
| Views of its own | none | Forms | none | none |

### 4.4 The Forms view (Access)

- **Made for about 150 forms, nested tens deep.** It opens on the tree, with a filter, and draws one form and what's
  inside it at a time. Every form is never drawn at once. Deep nesting opens a level at a time, and a form that holds
  itself, directly or through others, is shown once and marked.
- **Which form holds which:** a tree and a diagram of forms and the subforms inside them, to any depth, with the
  fields that link each subform to its parent. A form used in several places shows in each, and a form's panel lists
  where it's used.
- **A form's layout,** drawn from its controls' positions and sizes as a wireframe: sections (header, detail,
  footer), tab controls and their pages, option groups, subform controls showing the form they hold. Clicking a
  control selects it.
- **The side panel for a form or control:** its properties that matter (name, type, caption, what it's bound to, row
  source, default value, validation, visible and enabled, tab order), its events with the procedure or macro each
  runs, linked to the code, and its data source with the query's SQL.
- **The outline** (N5) puts those together as text, for one form and everything inside it.

### 4.5 Repository

```
shared/
  src/CodeDocumenter.Shared/        every tool: exit codes, version, files, command line, the page shell, d3
  src/CodeDocumenter.Workbooks/     vbadoc and pqdoc: opening and writing workbooks, importing
  src/CodeDocumenter.CodeViews/     vbadoc, accessdoc, csdoc and pythondoc: the code model, graph, class diagram, Calls
  src/CodeDocumenter.Vba/           vbadoc and accessdoc: reading the VBA language
vbadoc/  pqdoc/                     as now
accessdoc/  csdoc/  pythondoc/      each with its long name's .Cli, .Core, .Tests and .BrowserTests
codedocui/                          the desktop app, renamed
```

## 5. Decisions

### 5.1 The tools are in alpha

Agreed on 2026-11-13. Until a tool says otherwise:

- **A new version may change its pages, its output files and its page data** in any way. Documentation folders
  already committed to other repositories are made again with the new version, and their git histories will show the
  change. No code is written to read or tidy up what an older version made, beyond what's there now.
- **Remembered settings, saved views files and export manifests are kept working where it costs nothing,** and
  otherwise dropped, with a line in the work log saying so.
- **The same source still gives the same page** with the same version, and a page still never says when it was made.
  That costs nothing to keep and is what makes a page's diff in git mean something.
- **Steps that only move code** are checked by the tests passing and screenshots looked at in both themes. The
  byte-for-byte and pixel-by-pixel comparisons C1 and C2 used aren't needed.

### 5.2 Order of the work

Names first (K), so new code is born with the right ones. Then the page follow-ups (L), because they change the views
and the shell, and are better made before those are moved and shared than after. Then the shared views (M), which
every new tool stands on. Then Access (N), which reuses vbadoc's reading of VBA whole and adds forms; then C# (P),
the first language that isn't VBA and so the real test of the code model; then Python (Q). The desktop app last (R),
as it only runs what the tools can already do.

Each new tool begins with a spike that's tried on real code and on a work machine, as H1, I1 and J1 were. If a spike
says the recommended way won't do, the steps after it are rewritten before they're started.

### 5.3 The shared library in four

`ExcelDocumenter.Shared` holds several kinds of thing, and the new tools need only some of them:

- **`CodeDocumenter.Shared`:** what every tool needs. Exit codes, the build stamp, atomic and source folder writes,
  asking before overwriting, the command line's shared options, the page shell and d3.
- **`CodeDocumenter.Workbooks`:** what only vbadoc and pqdoc need. `Workbooks/` (with OpenMcdf), `Importing/`, and
  the `import` command's options.
- **`CodeDocumenter.CodeViews`:** what every tool but pqdoc needs. The code model and the views drawn from it.
- **`CodeDocumenter.Vba`** (from N2): the VBA language, out of vbadoc's Core. The parser, the analyser, the
  highlighter and how a module is written as a file. vbadoc keeps what's about workbooks; accessdoc brings what's
  about databases.

A tool's build stamp follows only the libraries it uses. The guidelines' rules stay: tools never use each other, and
code only one tool needs stays in that tool.

### 5.4 The code model

The page's data, in terms no language owns. A language's Core fills it in; the views draw it.

- **Files** are apart from what's in them. A VBA module is one file, but a C# type can be spread over several and one
  file can hold several types, and a Python file holds a module and its classes. Each unit and member says which file
  and lines it's in.
- **Groups, units and members,** as the table in 4.3 has them. Groups nest to any depth. Units are what the class
  diagram draws as boxes and the graph's upper level as nodes. That's what vbadoc has now, and every new language
  fits it, which is why the views can be shared without being rewritten.
- **Kinds are the language's:** each says what its kinds of unit, member and relationship are, with a label, a shape
  and a colour group, and the views take the legend, the Show… menu and the explorer's Types layout from that. New
  colours are validated with the dataviz palette validator.
- **Ids are names:** `Namespace.Type.Member(parameter types)` in C#, `package.module.Class.method` in Python, a
  module's or member's name in VBA. Favourites, hidden items, the filter and saved views are kept by id.
- **Edges** are kept between members, with their lines, and rolled up to units and (from P6) groups.
- **Calls and blocks** are as vbadoc has them: each call in the order of the code, with the `If`, loop or `switch`
  around it.
- **A language can add a view of its own,** as accessdoc adds Forms, using the shell and the selection like any other.

### 5.5 Reading Access

An Access database isn't a zip of parts like a workbook. It's a database file, with its forms and its VBA project
kept inside its own system tables in formats Microsoft hasn't published. There are three ways in:

| | How | For | Against |
|---|---|---|---|
| **A (recommended)** | Access writes each form and module out as text (its own `SaveAsText`), and accessdoc reads the text | The text is complete and readable: every section, control, property and event, and the form's module. It's the format Access's own source control tools use | Needs Access installed wherever the text is made. accessdoc has to start Access and stay out of the way of the database's start-up code |
| B | accessdoc reads only a folder of that text, made some other way (a script we supply, run from inside Access, or an existing add-in) | accessdoc itself needs no Access and runs nothing | A step by hand before every run |
| C | accessdoc reads the database file directly | No Access anywhere | The VBA can probably be reached, since it's stored much as a workbook's is. Forms are stored in a format nobody has documented, so this may never give the layout, which is the point |

The recommendation is A with B's folder as its other input, so the page can be made again, or made on a machine
without Access, from the text. How A stays safe:

- **The database given is never opened.** accessdoc copies it to a temporary folder and has Access open the copy, so
  the original isn't locked, changed or compacted.
- **None of the database's own code runs:** the copy is opened with its start-up form and `AutoExec` macro bypassed.
  N1 finds the way that works at home and at work, and if there isn't one, A is off and B stands.
- **Access is started hidden and closed again,** and a run that fails still closes it.
- **Linked tables aren't followed.** Only the front end's forms, modules, queries and macros are read.

N1 tries A and C on the samples, and reports. Only `.accdb` files with no password are in scope (1.5); the tool says
plainly when it's given an `.mdb`, a compiled database (`.accde`) or one that needs a password.

**The Access Version Control add-in** ([msaccess-vcs-addin](https://github.com/joyfullservice/msaccess-vcs-addin),
BSD-style licence, looked at on 2026-11-13) exports a database to a folder of text and builds one back from it. It's
VBA that runs inside Access as an installed add-in, so it can't be part of accessdoc, and it can't be assumed to be
installable at work. What it gives us:

- **It confirms choice A.** Its form and report files are Access's `SaveAsText` output, tidied. So the text accessdoc
  reads is the same whichever of the two made it.
- **Its export folder is another input.** `accessdoc <database>.src` reads a folder the add-in made: forms as
  `.form` with their code in `.cls` (or both in one `.bas`, in older exports), modules, queries as `.sql`, macros as
  `.macro`, and table definitions. Where the add-in has run, accessdoc needs no Access at all.
- **Its list of what to leave out.** `SaveAsText` writes things that change from one export to the next for no
  reason (printer settings, cached layout, checksums, name maps), and positions inside layout groups that depend on
  the screen scaling of the machine that exported them. The add-in's source says which, and how it settles them.
  accessdoc leaves the same things out of its source folder and its page, so the same database gives the same page
  on any machine.
- **Fixtures.** Its repository holds the exported source of a test database made for the purpose (forms, reports,
  macros, queries, local and linked tables), which can be committed here with its licence.

**Tables.** A database holds local tables and tables linked to other databases, and code that makes temporary
tables which may or may not be there when it's read (1.5):

- Tables are listed with whether each is local or linked. A linked table shows what it's linked to with any user
  name and password removed.
- A table or query that code, a form or a query names, but which isn't in the database, is shown as "not in the
  database when it was read". It's never an error.
- Table definitions go on the page but not into the source folder, so a temporary table left behind by a failed run
  doesn't appear in git. `--exclude` takes a pattern for names to leave off the page too.
- No table data is read, and linked tables are never opened.

**What's read from a form's text:** its record source and properties; its sections; every control with its type,
name, caption, position and size, what it's bound to, and its row source; tab controls and their pages, and option
groups, as controls holding controls; subform controls with the form each shows and the fields linking it to its
parent; every event property, which names an event procedure, a macro or an expression; and the form's own module,
which goes through the VBA library like any other.

**What forms add to the dependencies:** an event procedure is tied to its control, so it's an entry point with a
name; a subform control is a Holds edge between forms; `DoCmd.OpenForm "Name"` with a literal name, in VBA or a
macro, is an Opens edge; a record source or row source names a query or table. A form's name built from text at run
time can't be followed, and is counted.

### 5.6 Reading C#

Roslyn is Microsoft's own C# compiler as a library (MIT). The choices for using it:

| | How | For | Against |
|---|---|---|---|
| **A (recommended)** | Roslyn parses every file, and csdoc makes each project's compilation from what it reads in the solution and project files | Exact names within the code read: overloads, `var`, generics, extension methods, interfaces. Works with no SDK and no restore. Runs nothing | Reads project files itself, so it knows the common settings, not every one. Types from NuGet packages are unknown unless a restore has been done |
| B | Roslyn's MSBuild workspace loads the solution as Visual Studio does | Every setting right, every package known | Needs the SDK and a restore on the machine; runs the build's own steps; slow; awkward in a single-file executable |
| C | Roslyn's parser only, with our own name lookup, as vbadoc has | Smallest | Guesses where A knows |

Assuming P1 confirms A:

- **The solution file** gives the projects (`.sln` and `.slnx`). A `.csproj` alone is a solution of one.
- **A project's files** are every `.cs` under its folder, less `bin/`, `obj/` and what the project file removes. An
  older .NET Framework project file lists its own files, and that list is used as written (P7, after everything
  else in C#, as it's for reading other people's code).
- **A project's settings** come from its project file and the `Directory.Build.props` files above it, for the few
  that change what code means: the language version, `Nullable`, `ImplicitUsings`, defined constants, the root
  namespace, and references to other projects. When something more involved is met, the log says what was assumed.
- **References:** other projects in the solution; the .NET or .NET Framework reference assemblies, carried in the
  executable so no SDK is needed; and NuGet packages when a restore has left them (`obj/project.assets.json`, or an
  older project's `packages` folder). A type that can't be found doesn't stop anything: it's counted, and listed
  under `--verbose`.
- **Generated code** in `obj/` isn't read, apart from implicit usings. Razor and XAML aren't read.
- **Multi-targeted projects** are read once, for the first target framework.

### 5.7 Reading Python

**Python in plain words,** for the terms this plan uses:

| Word | Means | Nearest thing in VBA or C# |
|---|---|---|
| Module | One `.py` file. Its functions, classes and variables belong to it | A standard module; a C# file that is also a static class |
| Package | A folder of modules, usually with a file named `__init__.py` | A namespace or project |
| `import` | How one module names another to use it | A reference plus `using` |
| Class, method | As in C#. A method's first parameter, `self`, is the object | `Me`, `this` |
| Decorator | A line starting with `@` above a function, which hands it to something else. Frameworks use them to say "call this for that web address" or "this is a test" | An attribute such as `[HttpGet]` |
| Docstring | A text literal as the first line of a function, class or module, describing it | A `///` comment |
| Annotation | An optional note of a parameter's or variable's type, such as `count: int`. Python doesn't check it | A declared type |
| Virtual environment | A folder inside the project holding copies of installed packages (`venv`, `.venv`). Not the project's own code | `bin/` and `packages/` |

**The difficulty:** Python doesn't declare types. In `thing.save()`, nothing says what `thing` is, so which `save` it
calls has to be worked out from the code around it, and sometimes can't be. Imports are exact, and so is the
structure of every file. So the page's module-to-module picture is exact, and its member-to-member picture is as good
as the evidence in the code, with what couldn't be followed counted and listed, as vbadoc does late-bound calls.

**The choices for reading it:**

| | How | For | Against |
|---|---|---|---|
| **A (recommended)** | A tokeniser and structural parser of our own, in C# | Needs nothing installed. One self-contained executable | Ours to keep up with new syntax |
| B | Run the machine's own Python with a small script that prints the syntax tree | An exact syntax tree, for no parsing work | Needs Python on the machine and allowed to run there |
| C | A parser library | A full parse in .NET | A dependency whose grammar trails the language, or native code inside the executable |

**Nobody involved knows Python well** (1.7): you don't, and your colleague is new to it and inherited the project
from someone who's gone. So the tool has to be right without anyone to check it by eye, and the page is for finding
your way around code you didn't write: where it starts, which file uses which, and what a function leads to.
Nothing in the plan asks either of you to judge Python.

Assuming Q1 confirms A:

- **Checking it without a Python expert:** this machine has Python. While pythondoc is being built, and never when
  it's run, its reading of each file's structure is compared with what Python's own parser says, over the sample
  project and Python's standard library (thousands of files, in every style). Q1 sets that up, and it stays as an
  optional test that skips itself where Python isn't installed.
- **Finding the project:** packages are folders with `__init__.py`, or any folder of `.py` files under the root.
  `pyproject.toml` gives the project's name when there is one.
- **Structure** is exact: imports, classes with their bases, functions with their parameters, annotations, decorators
  and docstrings, module and class variables, and `self.` attributes set in a class's methods.
- **Names are followed by scope and import:** a local, an enclosing function, the module, then what it imported.
- **What a name holds is inferred from the evidence in the code:** an annotation, an assignment from a class's
  constructor, `self`, `cls` and `super()`. An attribute call on a value of unknown type isn't guessed at by name
  alone, since a name such as `run` or `get` can be in many classes: it's recorded as one that couldn't be followed.
- **Imports from outside the project** are listed as the outside packages a module uses.

### 5.8 What makes something an entry point

Each language's list of what's "called without a reference" (4.3) decides what's flagged as unreferenced and where
the Calls tab starts. For C# and Python the lists can't be complete, since frameworks call code by attributes,
decorators and naming conventions. So only what can't be reached from outside is ever flagged (private and internal
members in C#, names starting with an underscore in Python), the page's wording is "no reference found", and anything
carrying an attribute or decorator is treated as called. In Access, a procedure named for a control's event is only
an entry point if that control's event property says it runs event procedures, which the form's text tells us; vbadoc
has to guess that for workbooks.

### 5.9 Lock view and Peek

- **Lock view is the filter, plus a hold on the views.** Locking ticks every item now shown in the explorer (the
  shell's filter, which every view already follows), and stops a click from focusing, laying out, fitting or
  highlighting. Unlocking puts the ticks back as they were. So it needs no second idea of "what's shown", it's never
  silent (the explorer and each toolbar's filter note say what's kept), and a saved view keeps it.
- **A click while locked** selects, outlines the item, and shows its source. A click on empty space clears the
  selection, and the side panel closes unless it's locked open. Nothing else changes.
- **Peek is the shell's,** in the Uses / Used by list, so every tool has it. It reads the same source the panel does,
  so it needs the source loaded or embedded, and says so when it isn't.
- **Ctrl+click goes there,** which is what a plain click does now. That fits the guidelines' rule that Ctrl+click does
  the other thing.

### 5.10 Renaming without breaking anything

- **The pages' link to the repository** follows the new name. GitHub redirects the old address.
- **Folder renames are done with VS Code closed,** from a terminal of its own: the C# extensions hold the tool
  folders. K2 is one of these.
- **Old executables** stay in the old publish folder until deleted by hand; K3 says so.
- **The desktop app's remembered settings start afresh** under its new name (1.8).

## 6. Steps

### Step K0: Settle the questions

Done on 2026-11-13. The feedback plan is closed: its Later section moved to vbadoc's and pqdoc's plans and to
section 9 here, what pointed at it points here or at the tag `feedback-plan-final`, and the file is deleted. You
answered section 1, the plan was revised where an answer differed (the table at the top of section 1), and the
alpha decision (5.1) is in the guidelines.

### Step K1: Names inside the repository

The root solution, `ExcelDocumenter.Shared` (to `CodeDocumenter.Shared`, still one library until M1), the shell's
global, `ToolsPublishDir`, the build workflow, and every document that names them. The link to the repository waits
for K3.

### Step K2: The desktop app's name

`xldocui/` becomes `codedocui/` and `ExcelDocumenter.Desktop` becomes `CodeDocumenter.Desktop`. Until R1 it still
says it runs vbadoc and pqdoc. Done from a terminal outside VS Code.

### Step K3: The repository's name

Yours first, in this order, with a checklist written into this section when the step starts:

1. Rename the repository on GitHub.
2. Close VS Code, rename the local folder, and point the remote at the new address.
3. Copy Claude Code's memory folder for this repository to the name that matches the new path, or the next session
   starts without it.
4. Add the new publish folder to `PATH` on each machine, and delete the old one.

Then Claude: the pages' repository link, the READMEs, and a check that a fresh clone builds, tests and publishes.

### Step L1: Lock the view

As 1.3 and 5.9 set out, in the shell and in vbadoc's graph and class diagram and pqdoc's graph. Browser tests: with
the lock on, clicks on items and on empty space leave every node's place, the zoom and the set shown unchanged, and
change the source panel; unlocking puts the ticks back; a saved view keeps the lock. Screenshots in both themes.

### Step L2: Peek at a reference

As 1.4 and 5.9 set out, in the shell's Uses / Used by list. Browser tests in both tools: a click opens the preview
and leaves the selection; Go to and Ctrl+click navigate; with no source loaded, the preview says how to load it.

### Step L3: Calls tab follow-ups (E5b)

Raised on 2026-11-06 while H1 was under way.

- **Ifs with `Or` or `And` conditions seem not to show up, only the Else or ElseIf after them.** Looked at, not yet
  fixed. The parser reads every form of these, each block is right, and every call and dependency edge is found, so
  nothing is lost. The likely cause is in what the tab draws: a call in a condition belongs to the block around the
  If (`BlockTracker`: "a block's own statement is in the block around it"), and a block row is drawn only when its
  body holds a call (`childrenOf` in `calls.js`). So for this code, the tree shows IsA and IsB loose above the Else
  row, with no If row at all, and the same goes for a condition with one call:

  ```vb
  If Helper.IsA(1) Or Helper.IsB(2) Then
      x = 1
  Else
      Helper.DoB
  End If
  ```

  **First check this against your real example,** in case there's another cause. The likely fix: draw an If or
  ElseIf row when its condition makes a call, with those calls shown as the condition's, in the tree and the sequence
  diagram.
- **Keep the participants row in view while scrolling down the sequence diagram.** Each entry point is its own
  diagram with its own participants, so the row showing has to be that of the diagram being scrolled through, and it
  has to move sideways with the diagram.
- Unit tests for the condition's calls, browser tests for both, and screenshots in both themes.

### Step L4: Views file built into the page (G2)

- Each tool reads `<tool>-views.json` from the output folder, checks its format version, and builds it into the page,
  so readers get the views by opening the page and the file is versioned in git with the source. A missing file is
  normal; an unreadable one is a warning, not a failure.
- Views that mention items no longer in the source still load, and the missing items are listed, as now.
- Check that neither tool's source folder writer deletes the views file.

### Step L5: Printing and descriptions (G3)

- **Descriptions** come from a member's `VB_Description` attribute if it has one, otherwise from the comments directly
  above it, and from a module's comments at the top; for queries, from the description in the workbook, otherwise
  from comments at the top of the query. They're added to the page data in Core and shown in the side panel.
- **Printing** uses a print stylesheet and a Print menu: the current view or every view as a document, with the title,
  source and build stamp, then for each view its description, its diagram fitted to the page, and its items with
  kind, signature, description and Uses / Used by. Source is optional. The browser's Print, then Save as PDF, makes
  the PDF.
- Browser tests that print to PDF in headless Chromium and check the PDF has a page per view.
- You check printing to PDF and exporting views in the browser on a work machine.

### Step L6: Copy a view as text (G4)

A Copy button puts the current view on the clipboard as Markdown: its items with signatures and descriptions, the
open Calls tree, and optionally the source. It warns when the text is long. If the browser refuses the clipboard, the
text appears in a box to copy by hand. N5 uses the same button for a form's outline.

### Step M1: The shared library in three

Split as 5.3 sets out (the fourth library, for VBA, waits for N2). The shell's interface and messages say "source"
where they said "workbook" and mean either, with the tool supplying the word shown; the Export tab becomes something
a tool asks for; the summary's saved time becomes optional.

### Step M2: vbadoc's views move to the code views library

`graph.js`, `classes.js`, `calls.js`, the parts of `page.js` and `page.css` they need, and `ClassDiagram` move as
they are, still speaking of modules. vbadoc's browser tests move with them where they test the views, and stay where
they test vbadoc.

### Step M3: The code model

The model of 5.4, in C# and in the page's data, with vbadoc's Core filling it in. The views take kinds, labels,
shapes, relationships and wording from the language's description. A small made-up language in the library's own
tests (two kinds of unit, one extra relationship, a type in two files) proves the views don't assume VBA.

### Step N1: Sample databases, and a spike on reading them

- **Samples, with nothing for you to build by hand:**
  - The Version Control add-in's test source (5.5), committed as a fixture with its licence: real `SaveAsText` text
    for forms, reports, macros, queries and tables.
  - A small database Claude builds by script, through Access on this machine, from text written for the purpose: a
    form with a subform, a subform inside a subform, a continuous form, a tab control, an option group, a combo box
    with a row source, events run by event procedures, by a macro and by an expression, a standard module and a
    class module, a local table and a linked one, and a start-up form and an `AutoExec` macro that visibly do
    something, so the spike can prove they didn't run. What Access writes into it about you or your machine is
    removed before it's committed, with your agreement, as for the sample add-ins.
  - Northwind 2.0 Developer Edition as the larger test. You make it once from Access's own template (File, New,
    search for Northwind) into `accessdoc/private/`. It isn't committed.
- **Claude:** `spikes/n1-access`, throwaway. It tries 5.5's choice A (Access writes text from a copy, start-up
  bypassed, hidden, closed afterwards) and choice C (the VBA read from the file directly), and reports what each got
  and how long it took, on the samples and on a database of about 150 forms made by copying the sample's.
- **You:** run the spike at home and on a work machine, where application control, Group Policy and Access's own
  security settings are the real test, and on one real database.
- **The step ends with a recommendation, and N2–N5 are revised to fit.**

### Step N2: accessdoc reads a database's VBA

The VBA language moves out of vbadoc's Core into `CodeDocumenter.Vba`. `accessdoc/` is set up as the guidelines set
out for a tool. It gets the text as N1 settled, writes it into a source folder kept in step with the database, and
makes `<database>-access.html` with everything vbadoc's page has, form modules included. No Forms view yet.

### Step N3: accessdoc reads forms

The forms model, as 5.5 lists: sections, controls, subforms, data sources and events, read from each form's text.
Event procedures are tied to their controls, and Holds and Opens edges join the dependencies. Macros, queries and
tables are read as far as 1.5 and 5.5 say. Tests assert the sample's whole form tree and its edges.

The model keeps what a generator would need, not only what the page shows: each control's kind, its place and size
in Access's own units, its parent (section, tab page, option group), tab order, what it's bound to, its row source,
its attached label, and its events. Writing Blazor skeletons is in [Later](#9-later), and is to need no change to
what's read.

### Step N4: The Forms view

As 4.4: the tree and diagram of forms within forms, a form's layout drawn as a wireframe, and the side panel for a
form or control. A Forms layout in the explorer, down to controls. Browser tests and screenshots in both themes,
and a test on a generated database of 150 forms nested 30 deep, for speed and for a page that stays readable. Then
your review on a real database, which will add follow-up steps, as D1's did.

### Step N5: A form's outline, for rebuilding it

For a chosen form, everything inside it as text (1.5's row), through L6's Copy button and printed by L5's Print. You
try it on a real form you mean to rebuild, and say what a Blazor component needed that the outline didn't give. That
list is what the later step that writes Blazor skeletons starts from.

### Step P1: Spike on reading a C# solution

`spikes/p1-csharp`, throwaway. It reads a solution as 5.6 sets out and reports: projects, types and members found;
the share of names resolved, and what the unresolved ones were; time and memory; the published executable's size. Run
on this repository, restored and not, and by you on a real solution and on a work machine. It also tries the MSBuild workspace (choice B) on the same solutions, to say what A gives up. **The step
ends with a recommendation, and P2–P5 are revised to fit.**

### Step P2: csdoc reads a solution

`csdoc/` with its solution, documents and publish profile. A sample solution as a fixture: several projects, nested
and partial types, generics, overloads, records, interfaces, events and extension methods.
`csdoc <solution>` prints a summary of what it found; `--verbose` lists it. No page yet.

### Step P3: csdoc's dependencies

Edges between members with their lines, Inherits, Implements and Events, entry points as 5.8 has them, unreferenced
members, outside packages used, and the count of names that couldn't be followed. Tests assert the sample's whole
edge list, as vbadoc's do.

### Step P4: csdoc's page

`<solution>-csharp.html` on the shell and the code views: the explorer's three layouts, the graph at type and member
level, the class diagram, the source panel with a C# highlighter and Load folder, doc comment summaries in the side
panel, search. Then your review on a real solution.

### Step P5: csdoc's calls

Calls in order with the blocks around them; a call through an interface opens to its implementations, a virtual call
to its overrides; a call through a delegate or lambda is shown as one that couldn't be followed to a single place.

### Step P6: The graph at group level

A third level in the graph, above units: projects and namespaces in C#, packages in Python, `'@Folder` folders in
VBA, forms and modules in Access. Edges are rolled up, and opening a group shows what's in it. With it, a check on
one larger solution (a generated one, and someone else's if you have one to hand), so the README can say what size
the page is good for.

### Step P7: Older .NET Framework projects

Project files in the older style, which list their own files and references, and packages restored into a
`packages` folder. A fixture of one, the .NET Framework reference assemblies carried as the .NET ones are, and a
check on someone else's project if you have one to hand by then. Last in C#, because it's for reading other people's
code and none of yours needs it.

### Step Q1: Your colleague's project, a sample, and a spike

- **A copy of the project** goes into `pythondoc/private/`, which is git-ignored and never committed (1.7).
- **To ask your colleague,** with their answers written here. Each can be answered by someone new to Python; what
  needs Python knowledge, the spike finds out from the copy:

  | Question | Why it's asked | Their answer |
  |---|---|---|
  | How do you start it? The command you type or the file you run | The Calls tab starts from there | _(theirs)_ |
  | What does it do, in a sentence or two? | Says what a useful page would show first | _(theirs)_ |
  | Which parts do you need to change or understand first? | Decides what's checked most carefully | _(theirs)_ |
  | Is anything in the folder not part of it: old copies, downloads, data? | So it's left out | _(theirs)_ |

- **Claude:** a sample project as a fixture, covering packages, relative and star imports, classes with inheritance,
  decorators, properties, annotations and none, nested functions, and every form of string. `spikes/q1-python`,
  throwaway, reads it and reports what it read, what it passed over, and the share of attribute calls it could
  follow. On the copy it also reports what nobody can be asked: which version of Python the syntax needs, how the
  folder is laid out, how much of the code says what type things are, which frameworks and outside packages it
  imports, and where it looks to start from. The check against Python's own parser (5.7) is set up here.
- **You:** run the spike on the copy, on a work machine, since you may be the one running pythondoc for them.
- **The step ends with a recommendation, and Q2–Q4 are revised to fit.**

### Step Q2: pythondoc reads a project

`pythondoc/`, set up as the others were. Project discovery, the tokeniser and structural parser, and a console
summary.

### Step Q3: pythondoc's dependencies

Imports, scopes, what names hold (5.7), edges with lines, Inherits and Imports, entry points, outside packages, and
what couldn't be followed. Tests assert the sample's whole edge list.

### Step Q4: pythondoc's page and calls

`<project>-python.html`: the language's description, a Python highlighter, docstrings in the side panel, and the
Calls tab's blocks (`if`, loops, `try`, `with`, `match`). You make the page from the real project and your colleague
opens it; their review is whether it helps them find their way around, not whether the Python is right. Its README
is written for someone new to both Python and these tools.

### Step R1: The desktop app takes everything

The Document tab offers each tool that can read what was picked: a workbook gives vbadoc and pqdoc, a database
accessdoc, a solution csdoc, a folder whichever tools find something in it. The Import tab stays the Excel tools'.
Its test that every option is either offered or explained covers the new tools.

### Step R2: Finishing

Check every decision in section 5, and section 3, is in `docs/GUIDELINES.md` or a tool's `docs/DECISIONS.md`, move
[Later](#9-later) into the relevant plans, update the root README, then delete this file in its own commit.

## 7. Changes to existing documents

Approving this plan covers the changes below, each made in the step that causes it. Anything else still needs asking.

| Document | Change | Steps |
|---|---|---|
| `docs/GUIDELINES.md` | The tools are in alpha (5.1), which loosens "values stored in the data of generated pages keep their spelling" and the rule on deleting older outputs | K0 |
| `docs/GUIDELINES.md` | Naming: the new names and tools. Structure: the shared libraries and which tool uses which. Versions: a stamp follows the libraries its tool uses | K1–K3, M1, N2, P2, Q2 |
| `docs/GUIDELINES.md` | HTML pages: Lock view; Peek, and Ctrl+click in the Uses / Used by list; views files built in; printing; "source" in place of "workbook"; the Export tab as the Excel tools' | L1, L2, L4, L5, M1 |
| `vbadoc/Requirements.md`, `vbadoc/docs/DECISIONS.md`, `vbadoc/README.md` | Lock view, Peek, the Calls tab's condition rows, views built in, printing and descriptions, copying a view; where its views and its VBA reading now live | L1–L6, M2, M3, N2 |
| `pqdoc/Requirements.md`, `pqdoc/docs/DECISIONS.md`, `pqdoc/README.md` | Lock view, Peek, views built in, printing and descriptions, copying a view | L1, L2, L4–L6 |
| The desktop app's documents | Its new name; then the tools it runs | K2, R1 |
| `shared/README.md` | Split in step with the libraries | M1, N2 |
| New | `Requirements.md`, `DECISIONS.md`, `PLAN.md`, `README.md` and `CLAUDE.md` for accessdoc, csdoc and pythondoc | N2, P2, Q2 |
| Root `README.md` and `CLAUDE.md` | The new names, and the new tools as they arrive | K1, K3, N2, P2, Q2 |

## 8. What you'll be asked to do

| When | What |
|---|---|
| K2 | Close VS Code while the app's folder is renamed |
| K3 | Rename the repository on GitHub and on disk, move Claude's memory folder, update `PATH` |
| L3 | Find the real example of the missing Ifs |
| L5 | Print to PDF on a work machine |
| N1 | Make Northwind 2.0 from Access's template into `accessdoc/private/`; run the spike at home, on a work machine and on one real database |
| N4, N5 | Try the Forms view and the outline on a real database and a form you mean to rebuild |
| P1 | Run the spike on a real solution, and on a work machine |
| P4 | Try csdoc's page on a real solution |
| Q1 | Put a copy of your colleague's project in `pythondoc/private/`, ask them four questions, and run the spike on a work machine |
| Q4 | Make the page for your colleague, who opens it and says whether it helps |
| Every step | Review, rebase onto main and push |

## 9. Later

Left out to keep this plan to what was asked for.

**New with this plan**

- **Writing Blazor component skeletons** from the forms model: a component for each form and subform, with its
  controls laid out and bound, and its events as empty handlers naming the VBA they replace. Asked for on
  2026-11-13 as very helpful. N3 keeps the model complete enough for it, and N5's review says what it needs.
- **Access reports,** read as forms are.
- **Older `.mdb` files,** and saying more about compiled and password-protected databases.
- **Moving forms and modules between Access databases,** as `import` does for workbooks.
- **A table relationships diagram** for Access.
- **vbadoc's UserForms (its stage 6) on the Forms view.**
- **csdoc reading Razor components (`.razor`),** so a Blazor project's components show with the C# behind them.
- **pqdoc on the code views,** if a query graph and a code graph turn out to want the same things.
- **One page for a repository that holds several languages,** and links between them.
- **The git commit in the top bar's summary,** read from `.git` without running git.
- **Outside packages as nodes in the graph,** not only a list.
- **Other languages:** VB.NET (Roslyn reads it, so it's the cheapest), F#, TypeScript, Jupyter notebooks.
- **Python stub files (`.pyi`) and installed packages' types** as more evidence for what a name holds.
- **csdoc reading what source generators make,** from `obj/`, when it's there.

**From the feedback plan's Later section** (what's about one tool is in that tool's `docs/PLAN.md`)

- **One executable** (`codedoc vba`, `codedoc pq`, and so on), a command that documents a whole workbook on one page,
  and links from VBA to the queries it refreshes.
- **Export a view as a `.drawio` file,** if draw.io turns out to be available at work.
- **Make names inside the source view links,** in every tool.
