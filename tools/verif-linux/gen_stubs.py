import re, sys, pathlib, xml.etree.ElementTree as ET
src = pathlib.Path(sys.argv[1]); out = pathlib.Path(sys.argv[2]); out.mkdir(parents=True, exist_ok=True)
X = '{http://schemas.microsoft.com/winfx/2006/xaml}'
for f in src.rglob('*.xaml'):
    root = ET.parse(f).getroot()   # also validates XML well-formedness
    # WPF (MC3088) : les éléments-propriétés (<X.Y>) doivent être avant ou après le contenu, jamais au milieu.
    for el in root.iter():
        kinds = ['p' if '.' in c.tag.split('}')[-1] else 'c' for c in el]
        seq = ''.join(k for i, k in enumerate(kinds) if i == 0 or kinds[i - 1] != k)
        if 'cpc' in seq:
            sys.exit(f'{f.name} : élément-propriété au milieu du contenu de <{el.tag.split("}")[-1]}> (erreur MC3088)')
    cls = root.get(X+'Class')
    if not cls: continue
    ns, name = cls.rsplit('.', 1)
    fields = []
    # named elements outside templates are fields
    def walk(el, in_template):
        uri, _, tag = el.tag[1:].partition('}') if el.tag.startswith('{') else ('', '', el.tag)
        tmpl = in_template or tag in ('DataTemplate','ControlTemplate','HierarchicalDataTemplate')
        n = el.get(X+'Name')
        if n and not tmpl and '.' not in tag:
            m = re.match(r'clr-namespace:([\w.]+)', uri)
            fields.append(((m.group(1) if m else 'System.Windows.Controls') + '.' + tag, n))
        for c in el: walk(c, tmpl)
    walk(root, False)
    base = root.tag.split('}')[-1]
    body = ''.join(f'    internal {t} {n} = null!;\n' for t, n in fields)
    extra = '    public static void Main() { }\n' if base == 'Application' else '    public void InitializeComponent() { }\n'
    (out / (name + '.g.cs')).write_text(f'namespace {ns};\npartial class {name}\n{{\n{body}{extra}}}\n')
    print(f.name, '->', len(fields), 'fields')
