# Translating Modern XAMPP Control Panel

Every piece of text the panel shows lives in one JSON file per language in `Lang/`. `Lang/en.json` is the source
of truth; a translation is a copy of it with the right-hand sides translated.

## Add or improve a language

1. Copy `Lang/en.json` to `Lang/<code>.json`, where `<code>` is the language code Windows uses: `de`, `fr`, `es`,
   `pt-BR`, `nb`... A plain language code (`de`) serves every region (`de-AT`, `de-CH`) unless a more specific file
   exists.
2. Set `"$name"` to the language's own name, as it should appear in Config (`"Deutsch"`, `"Norsk bokmål"`).
3. Translate the values. Leave the keys alone.
4. Try it without building: put the file in a `lang` folder next to `XamppControl.exe` (or in
   `%AppData%\XamppControl\lang`), start the panel, and choose the language under **Config › Language**. Open
   windows switch at once.
5. Send a pull request with the file in `Lang/`. `dotnet test XamppControl.slnx` checks it (see below).

You don't have to translate everything. A key you leave out, or delete, shows in English.

## Rules for the values

- **Placeholders** such as `{0}` and `{1}` stand for a value the panel fills in (a version, a file name, a count).
  Keep every placeholder the English text has; you may move them around. `{0:P0}` and similar keep their format
  part. To show a literal brace, write `{{` or `}}`.
- **Plurals** come as two keys, `Something.One` and `Something.Other`. `.One` is used for exactly 1, `.Other` for
  everything else, and `{0}` is the count in both.
- **Don't translate** product and program names (Apache, MySQL, MariaDB, PHP, Xdebug, Mailpit, FileZilla, XAMPP,
  IIS), file names and paths (`php.ini`, `httpd-vhosts.conf`, `xampp\mysql_dumps`), settings names
  (`sendmail_path`), commands, and addresses (`https://myproject.test`, `localhost:8025`).
- Column headers are upper-case in English (`MODULE`, `ACTIONS`); follow what looks natural in your language.
- Keep `…` (one character) where English has it; it marks buttons that open a dialog and running actions.
- Buttons are short. If a translation is much longer than the English, look for a shorter word before the layout
  has to stretch.
- JSON needs `\"` for a quote inside a value and `\\` for a backslash, exactly as in `en.json`.

## What is not translated

The log (the panel at the bottom of the main window, the Logs window and `xampp-control.log`) stays in English in
every language. Those lines are what people paste into bug reports and search for, and several of them are XAMPP's
own wording.

## For developers

- XAML: `Text="{DynamicResource Area.Key}"`. Code: `Loc.T("Area.Key")`, `Loc.T("Area.Key", value)`,
  `Loc.Plural("Area.Key", count)`. Add the English text to `Lang/en.json` in the same change.
- Keys are `Area.PascalCaseWords`, where the area is the window or feature (`Main`, `Sites`, `Updates`,
  `Common` for text shared by several windows).
- Never build a sentence from translated fragments; give it one key with placeholders, so translators can reorder.
- `Loc.Apply` publishes the strings as application resources, which is why `DynamicResource` text changes live.
  Text a view model computed earlier is refreshed the next time it is rebuilt; subscribe to `Loc.Changed` if a
  property must follow at once.
- `tests/XamppControl.Tests/LocalizationTests.cs` fails the build when a key used in XAML or code is missing from
  `en.json`, when a translation has a key English doesn't, or when its placeholders differ.
