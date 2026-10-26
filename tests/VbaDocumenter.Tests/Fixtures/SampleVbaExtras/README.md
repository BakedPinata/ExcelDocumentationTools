# SampleVbaExtras

Hand-written VBA that covers what `SampleVba` doesn't: `Const` (several per line, typed, with type
characters), `Enum` (comments, bracketed names), `Type`, `Declare` (`PtrSafe`, `Alias`, inside `#If`),
type-declaration characters, line continuations, `Rem` comments, `Optional`/`ParamArray`/array parameters,
`Static` and `Friend` procedures, `Property Get/Let/Set`, `Attribute` lines inside procedures, a time
literal containing `:`, labels and line numbers.

Unlike SampleVba, these files use CRLF line endings and the headers the VBA editor writes on export
(`Settings.cls` has the class header, `OrderForm.frm` a form designer block), so the parser's line
numbers are tested against files laid out exactly as the tool writes them.
