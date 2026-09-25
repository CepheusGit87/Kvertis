#!/usr/bin/env python3
"""Consistency check for the Kvertis.App resources and XAML (runs on Linux, no Windows tools needed).

Fails when
  1. a .resw file is not well-formed, has empty values, or the language files differ in keys or placeholders,
  2. an x:Uid used in XAML has no resource, or a resource targets an x:Uid/property that does not exist
     (a wrong property name in a .resw makes WinUI throw at runtime),
  3. a resource key used in C# (string literal such as "Main_Start_Button") or in the manifest is missing,
     including the dynamic families Error_{ConversionErrorCode}_Title/_Body, Warning_{InputWarning},
     Card_State_{JobItemState}, Preset_{ConversionPreset}, Pro_Purchase_{PurchaseOutcome},
  4. a resource key is defined but never used,
  5. an interactive control has no AutomationProperties.Name (literal, x:Bind or x:Uid resource),
  6. a {StaticResource}/{ThemeResource} key is neither defined in the app's XAML nor in WinUI's generic.xaml
     (checked only when the Windows App SDK package is in the local NuGet cache).

Usage: python3 tools/compliance/check-resw.py [--app src/Kvertis.App]
"""
import argparse
import glob
import os
import re
import sys
import xml.etree.ElementTree as ET

X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
AN = '[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name'
TT = '[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip'
LANGUAGES = ['de-DE', 'en-US']

# Properties a .resw entry may set per element type (x:Uid). Anything else is reported.
ALLOWED = {
    'TextBlock': {'Text'},
    'Button': {'Content', AN, TT},
    'HyperlinkButton': {'Content', AN, TT},
    'DropDownButton': {'Content', AN, TT},
    'ComboBoxItem': {'Content'},
    'ComboBox': {AN, 'Header', 'PlaceholderText'},
    'RadioButton': {'Content', AN},
    'CheckBox': {'Content', AN},
    'ListView': {AN},
    'ProgressRing': {AN},
    'ProgressBar': {AN},
    'Slider': {'Header', AN},
    'NumberBox': {'Header', 'PlaceholderText', AN},
    'ToggleSwitch': {'Header', 'OnContent', 'OffContent', AN},
    'TextBox': {'Header', 'PlaceholderText', AN},
    'SettingsCard': {'Header', 'Description', AN},
    'InfoBar': {'Title', 'Message'},
    'Expander': {AN},
    'ContentDialog': {'Title', 'CloseButtonText', 'PrimaryButtonText', 'SecondaryButtonText'},
    'Image': {AN},
    'MediaPlayerElement': {AN},
    'ScrollViewer': {AN},
}

INTERACTIVE = {'Button', 'HyperlinkButton', 'DropDownButton', 'SplitButton', 'ToggleButton', 'ToggleSwitch', 'ComboBox',
               'Slider', 'NumberBox', 'TextBox', 'PasswordBox', 'ListView', 'GridView', 'CheckBox', 'RadioButton',
               'AppBarButton', 'AppBarToggleButton', 'Expander'}

# Code keys built at runtime: prefix -> enum whose members complete the key.
DYNAMIC = {
    'Card_State_': 'JobItemState',
    'Warning_': 'InputWarning',
    'Preset_': 'ConversionPreset',
    'Pro_Purchase_': 'PurchaseOutcome',
    'Grade_Band_': 'GradeBand',
    'Target_Kind_': 'MediaKind',
}
# Families whose key is prefix + enum member + suffix (step 2: "Effect_ResolutionReduced_Text").
DYNAMIC_SUFFIX = {
    ('Effect_', '_Text'): 'EffectCode',
    ('Zone_', '_Name'): 'TuningAspect',
}
KEY_LITERAL = re.compile(r'"((?:App|About|Card|Convert|Dialog|Effect|Error|Format|FormatPicker|Grade|History|Licenses|Main|More|Preset|Preview|Pro|Settings|Steps|Target|Warning|Window|Zone)_[A-Za-z0-9_]*)"')
RESOURCE_REF = re.compile(r'\{(?:StaticResource|ThemeResource)\s+([A-Za-z0-9_.]+)\s*\}')
PLACEHOLDER = re.compile(r'\{(\d+)\}')

failures = []


def fail(message):
    failures.append(message)


def load_resw(path):
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as ex:
        fail(f'{path}: not well-formed XML ({ex})')
        return {}
    values = {}
    for data in root.findall('data'):
        name = data.get('name')
        value = data.findtext('value')
        if name in values:
            fail(f'{path}: duplicate key {name}')
        if value is None or not value.strip():
            fail(f'{path}: empty value for {name}')
        values[name] = value or ''
    return values


def enum_members(repo, name):
    pattern = re.compile(r'enum\s+' + re.escape(name) + r'\s*\{(.*?)\}', re.S)
    for path in glob.glob(os.path.join(repo, 'src', '**', '*.cs'), recursive=True):
        with open(path, encoding='utf-8') as f:
            match = pattern.search(f.read())
        if match:
            body = re.sub(r'//[^\n]*|/\*.*?\*/', '', match.group(1), flags=re.S)
            body = re.sub(r'\[[^\]]*\]', '', body)
            return [m.split('=')[0].strip() for m in body.split(',') if m.split('=')[0].strip()]
    fail(f'enum {name} not found under src/')
    return []


def local(tag):
    return tag.split('}')[-1]


def content_children(el):
    return [c for c in el if '.' not in local(c.tag)]


def check_scope_collisions(names):
    """PRI278: a key must not be both a plain resource ("Foo") and a scope ("Foo.Text")."""
    bare = {n for n in names if '.' not in n}
    scoped = {n.split('.')[0] for n in names if '.' in n}
    return sorted(bare & scoped)  # scope-collision


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--app', default='src/Kvertis.App')
    args = parser.parse_args()
    repo = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
    app = os.path.join(repo, args.app)
    if not os.path.isdir(app):
        print(f'{args.app} does not exist; nothing to check.')
        return 0

    # 1. resource files
    tables = {}
    for lang in LANGUAGES:
        path = os.path.join(app, 'Strings', lang, 'Resources.resw')
        if not os.path.exists(path):
            fail(f'missing {os.path.relpath(path, repo)}')
            continue
        tables[lang] = load_resw(path)
    if len(tables) != len(LANGUAGES):
        return report()
    keys = set(tables[LANGUAGES[0]])
    for k in check_scope_collisions(keys):
        fail(f'PRI278: {k} is both a plain resource and an x:Uid scope; rename the plain key (e.g. {k}Text)')
    for lang in LANGUAGES[1:]:
        other = set(tables[lang])
        for k in sorted(keys - other):
            fail(f'{lang}: key missing: {k}')
        for k in sorted(other - keys):
            fail(f'{LANGUAGES[0]}: key missing: {k}')
    for k in sorted(keys & set(tables[LANGUAGES[1]])):
        a = sorted(set(PLACEHOLDER.findall(tables[LANGUAGES[0]][k])))
        b = sorted(set(PLACEHOLDER.findall(tables[LANGUAGES[1]][k])))
        if a != b:
            fail(f'placeholders differ for {k}: {LANGUAGES[0]} {a} vs {LANGUAGES[1]} {b}')

    # 2. XAML x:Uid usage
    uid_types = {}
    uid_has_content = {}
    xaml_keys = set()
    resource_refs = []
    for path in sorted(glob.glob(os.path.join(app, '**', '*.xaml'), recursive=True)):
        if f'{os.sep}obj{os.sep}' in path or f'{os.sep}bin{os.sep}' in path:
            continue
        rel = os.path.relpath(path, repo)
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as ex:
            fail(f'{rel}: not well-formed XML ({ex})')
            continue
        with open(path, encoding='utf-8') as f:
            text = f.read()
        resource_refs += [(rel, m) for m in RESOURCE_REF.findall(text)]
        for el in root.iter():
            key = el.get(X + 'Key')
            if key:
                xaml_keys.add(key)
            tag = local(el.tag)
            uid = el.get(X + 'Uid')
            if uid:
                uid_types.setdefault(uid, set()).add(tag)
                uid_has_content[uid] = uid_has_content.get(uid, False) or bool(content_children(el))
            # 5. automation names on interactive controls
            interactive = tag in INTERACTIVE or (tag == 'SettingsCard' and el.get('IsClickEnabled') == 'True')
            if interactive:
                named = any(local(a) == 'AutomationProperties.Name' for a in el.attrib)
                if not named and uid and f'{uid}.{AN}' in keys:
                    named = True
                if not named:
                    fail(f'{rel}: <{tag}{" x:Uid=" + uid if uid else ""}> has no AutomationProperties.Name')

    for uid, types in sorted(uid_types.items()):
        own = [k for k in keys if k.startswith(uid + '.')]
        if not own:
            fail(f'x:Uid "{uid}" has no resource')
        for k in own:
            prop = k[len(uid) + 1:]
            for t in types:
                allowed = set(ALLOWED.get(t, set()))
                if uid_has_content.get(uid):
                    allowed.discard('Content')
                if prop not in allowed:
                    fail(f'resource {k}: property "{prop}" is not allowed on <{t}> (would fail at runtime or hide content)')
    for k in sorted(keys):
        if '.' in k and k.split('.', 1)[0] not in uid_types:
            fail(f'resource {k}: no element with x:Uid "{k.split(".", 1)[0]}"')

    # 3. keys used from code and manifest
    used = set()
    for path in glob.glob(os.path.join(app, '**', '*.cs'), recursive=True):
        if f'{os.sep}obj{os.sep}' in path or f'{os.sep}bin{os.sep}' in path:
            continue
        with open(path, encoding='utf-8') as f:
            prefixes = set(DYNAMIC) | {p for p, _ in DYNAMIC_SUFFIX}
            for literal in KEY_LITERAL.findall(f.read()):
                if literal.endswith('_'):
                    if literal not in prefixes and not literal.startswith('Error_'):
                        fail(f'{os.path.relpath(path, repo)}: dynamic key prefix "{literal}" is unknown to check-resw.py')
                    continue
                used.add(literal)
    for path in glob.glob(os.path.join(app, '*.appxmanifest')):
        with open(path, encoding='utf-8') as f:
            used |= set(re.findall(r'ms-resource:([A-Za-z0-9_]+)', f.read()))
    for prefix, enum in DYNAMIC.items():
        for member in enum_members(repo, enum):
            used.add(prefix + member)
    for (prefix, suffix), enum in DYNAMIC_SUFFIX.items():
        for member in enum_members(repo, enum):
            used.add(prefix + member + suffix)
    for member in enum_members(repo, 'ConversionErrorCode'):
        used.add(f'Error_{member}_Title')
        used.add(f'Error_{member}_Body')
    for k in sorted(used - keys):
        fail(f'resource key used but not defined: {k}')

    # 4. unused keys
    for k in sorted(keys):
        if '.' not in k and k not in used:
            fail(f'resource key defined but never used: {k}')

    # 6. theme resources
    generic = sorted(glob.glob(os.path.expanduser(
        '~/.nuget/packages/microsoft.windowsappsdk.winui/*/lib/*/Microsoft.WinUI/Themes/generic.xaml')))
    if generic:
        with open(generic[-1], encoding='utf-8') as f:
            winui_keys = set(re.findall(r'x:Key="([^"]+)"', f.read()))
        for rel, key in resource_refs:
            if key not in xaml_keys and key not in winui_keys:
                fail(f'{rel}: resource key "{key}" is not defined by the app or WinUI')
    else:
        print('note: WinUI generic.xaml not in the NuGet cache; theme resource keys not checked')

    return report(len(keys), len(uid_types), len(used))


def report(keys=0, uids=0, used=0):
    if failures:
        for message in failures:
            print('FAIL:', message)
        print(f'check-resw: {len(failures)} problem(s).')
        return 1
    print(f'check-resw: OK ({keys} keys per language, {uids} x:Uid, {used} code/manifest keys).')
    return 0


if __name__ == '__main__':
    sys.exit(main())
