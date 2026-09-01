#!/usr/bin/env python3
"""Операция lint из ../CLAUDE.md — проверка здоровья вики.

Проверяет:
  1. битые относительные ссылки между страницами;
  2. страницы-сироты (недостижимые из index/overview/log/CLAUDE);
  3. согласованность sources/ и raw/;
  4. УСТАРЕВШИЕ ПУТИ — каждый процитированный артефакт кода сверяется с рабочей копией;
  5. заглушки: status: stub и блоки «не проверено»;
  6. дыры: термин упоминают N страниц, а своей страницы нет.

Слой raw/ неизменяем — ссылки внутри него не проверяются (это текст источников).

    python scripts/lint.py            отчёт
    python scripts/lint.py --quiet    только проблемы
Код возврата 1, если есть битые ссылки или несуществующие пути.
"""
import io, os, re, sys, glob, argparse

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

SCRIPTS = os.path.dirname(os.path.abspath(__file__))
VAULT   = os.path.dirname(SCRIPTS)
REPO    = os.path.dirname(VAULT)

CODE_EXT = r"\.(cs|ps1|vue|ts|js|props|yml|yaml|targets|csproj|sln|json|md)(:[\d\-,]+)?$"
CODE_DIR = r"^(src|docs|\.github|samples|img|libs)/"
# Плейсхолдеры из примеров в тексте — не пути.
SKIP = {"path/File.cs", "kebab-case.md", "wiki/Home.md", "wiki/Writing-extensions-with-AI.md"}

# Файлы, создаваемые в рантайме (в профиле пользователя), а не лежащие в репозитории.
RUNTIME = {"ai-providers.json", "mcp.json", "index.db", "registry.json"}

# Существуют, но не в текущей ветке. Значение — где искать; это не ошибка.
ELSEWHERE = {"docs/pipeline-design.md": "ветка claude/pipelines-plan-f8jrgf"}

# Дыры: термин -> страница, которая им владеет (None = владельца нет).
# Дополняйте по мере роста вики.
TERMS = {
    "лента / карточки":  (r"ленту|ленты|лента\b|карточк",            "wiki/concepts/inbox-and-cards.md"),
    "теневой индекс":    (r"теневой индекс|теневого индекса",        "wiki/entities/shadow-index.md"),
    "CommandQueue":      (r"CommandQueue",                            "wiki/entities/command-queue.md"),
    "модуль проверки":   (r"модул[а-я]+ проверки|свод[а-я]* правил", "wiki/analyses/checking-module.md"),
    "порог прерывания":  (r"порог[а-я]* прерывания",                  "wiki/concepts/proactivity-budget.md"),
    "папка проекта":     (r"папк[а-я]+ проекта",                      "wiki/entities/project-folder.md"),
    "конвейер":          (r"конвейер",                                "wiki/sources/pipeline-design-doc.md"),
}

def wiki_pages():
    out = []
    for p in glob.glob(os.path.join(VAULT, "**", "*.md"), recursive=True):
        rel = os.path.relpath(p, VAULT).replace(os.sep, "/")
        if rel.startswith(("raw/", ".obsidian/", "scripts/")):
            continue
        out.append((rel, p))
    return sorted(out)

def read(p):
    return io.open(p, encoding="utf-8").read()

def check_links(pages):
    bad = []
    for rel, p in pages:
        base = os.path.dirname(p)
        for m in re.finditer(r"\]\(([^)\s]+)\)", read(p)):
            t = m.group(1)
            if t.startswith(("http", "#", "mailto:")):
                continue
            if not os.path.exists(os.path.normpath(os.path.join(base, t))):
                bad.append((rel, t))
    return bad

def check_orphans(pages):
    linked = set()
    for entry in ("wiki/index.md", "wiki/overview.md", "wiki/log.md", "CLAUDE.md"):
        p = os.path.join(VAULT, entry)
        if not os.path.exists(p):
            continue
        base = os.path.dirname(p)
        for m in re.finditer(r"\]\(([^)\s]+)\)", read(p)):
            t = m.group(1)
            if t.startswith(("http", "#")):
                continue
            linked.add(os.path.normpath(os.path.join(base, t)))
    return [rel for rel, p in pages
            if rel.startswith("wiki/") and os.path.basename(rel) != "index.md"
            and os.path.normpath(p) not in linked]

def resolve(bare):
    cands = [bare] if re.match(CODE_DIR, bare) else [bare, "src/" + bare]
    for c in cands:
        if os.path.exists(os.path.join(REPO, c.replace("/", os.sep))):
            return c
    tail = bare.split("/")[-1]
    hits = []
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin", "node_modules", "LLM Wiki")]
        if tail in files:
            q = os.path.join(root, tail)
            if "/" in bare and bare.replace("/", os.sep) not in q:
                continue
            hits.append(q)
    return os.path.relpath(hits[0], REPO).replace(os.sep, "/") if hits else None

def check_code_paths(pages):
    seen = {}
    for rel, p in pages:
        for m in re.finditer(r"`([^`\n]+)`", read(p)):
            t = m.group(1).strip()
            if t in SKIP or t in RUNTIME or "<" in t or ">" in t:
                continue
            if t.startswith("../") or t.endswith("/") or "→" in t or "->" in t or " " in t:
                continue
            if re.search(CODE_EXT, t) or re.match(CODE_DIR, t):
                if t.endswith(".md") and not re.match(CODE_DIR, t):
                    continue          # внутренние ссылки вики проверяет check_links
                seen.setdefault(t, set()).add(rel)
    missing, ok, elsewhere = [], [], []
    for t, where in sorted(seen.items()):
        bare = t.split(":")[0]
        if bare in ELSEWHERE:
            elsewhere.append((t, ELSEWHERE[bare]))
        elif resolve(bare):
            ok.append((t, sorted(where)))
        else:
            missing.append((t, sorted(where)))
    return missing, ok, elsewhere

def check_sources_raw(pages):
    notes = []
    srcs = {os.path.basename(r) for r, _ in pages if r.startswith("wiki/sources/")}
    raws = {os.path.basename(p) for p in glob.glob(os.path.join(VAULT, "raw", "*"))
            if os.path.isfile(p) and os.path.basename(p) != "README.md"}
    body = " ".join(read(p) for r, p in pages if r.startswith("wiki/sources/"))
    for f in sorted(raws):
        if f not in body:
            notes.append("файл в raw/ не упомянут ни одной страницей sources/: " + f)
    return notes, sorted(srcs), sorted(raws)

def check_stubs(pages):
    out = []
    for rel, p in pages:
        if rel in ('CLAUDE.md', 'wiki/conventions.md'):
            continue          # они ДОКУМЕНТИРУЮТ разметку, а не используют её
        s = read(p)
        n = len(re.findall(r"\[!warning\]", s))
        if re.search(r"^status:\s*stub\s*$", s, re.M):
            out.append((rel, "status: stub", n))
        elif n:
            out.append((rel, "", n))
    return out

def check_gaps(pages):
    out = []
    for term, (rx, owner) in TERMS.items():
        hits = [rel for rel, p in pages if re.search(rx, read(p), re.I)]
        out.append((term, len(hits), owner))
    return sorted(out, key=lambda x: -x[1])


def main():
    ap = argparse.ArgumentParser(description="lint вики")
    ap.add_argument("--quiet", action="store_true", help="только проблемы")
    args = ap.parse_args()

    pages = wiki_pages()
    fail = 0
    say = (lambda *a: None) if args.quiet else print

    say(f"Вики: {VAULT}")
    say(f"Репозиторий: {REPO}")
    say(f"Страниц: {len(pages)}\n")

    bad = check_links(pages)
    if bad:
        fail = 1
        print(f"[БИТЫЕ ССЫЛКИ] {len(bad)}")
        for rel, t in bad:
            print(f"   {rel} -> {t}")
    else:
        say("[ссылки] битых нет")

    orph = check_orphans(pages)
    if orph:
        print(f"[СИРОТЫ] {len(orph)} — недостижимы из index/overview/log/CLAUDE")
        for r in orph:
            print("   " + r)
    else:
        say("[сироты] нет")

    missing, ok, elsewhere = check_code_paths(pages)
    if missing:
        fail = 1
        print(f"[НЕТ В РЕПОЗИТОРИИ] {len(missing)} из {len(missing)+len(ok)} процитированных артефактов")
        for t, where in missing:
            print(f"   {t}\n      цитируют: {', '.join(where)}")
    else:
        say(f"[пути] все {len(ok)} процитированных артефакта на месте")
    for t, why in elsewhere:
        say(f"[пути] {t} — не в этой ветке, ожидаемо ({why})")

    notes, srcs, raws = check_sources_raw(pages)
    if notes:
        print("[SOURCES <-> RAW]")
        for n in notes:
            print("   " + n)
    else:
        say(f"[sources/raw] согласовано: {len(srcs)} страниц, {len(raws)} файлов в raw/")

    stubs = check_stubs(pages)
    if stubs and not args.quiet:
        print(f"\n[НЕЗАКРЫТОЕ] {len(stubs)} страниц")
        for rel, st, n in stubs:
            mark = f"{st}, " if st else ""
            print(f"   {rel}  ({mark}блоков «не проверено»: {n})")

    if not args.quiet:
        print("\n[ДЫРЫ] термин — страниц упоминают — своя страница")
        for term, n, owner in check_gaps(pages):
            print(f"   {term:22} {n:>3}   {owner or '— НЕТ'}")

    if fail:
        print("\nlint: есть проблемы, требующие правки")
    else:
        say("\nlint: чисто")
    return fail


if __name__ == "__main__":
    sys.exit(main())
