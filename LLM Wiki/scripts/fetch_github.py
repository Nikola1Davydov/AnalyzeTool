#!/usr/bin/env python3
"""Снимки трекера задач в raw/ — операция ingest из ../CLAUDE.md.

Публичный API, без токена и без зависимостей (только stdlib).
Пишет ДАТИРОВАННЫЕ файлы и НИКОГДА не перезаписывает существующий снимок:
слой raw/ неизменяем.

    python scripts/fetch_github.py                  issues + комментарии
    python scripts/fetch_github.py --issues         только тела issues
    python scripts/fetch_github.py --comments       только комментарии
    python scripts/fetch_github.py --date 2026-09-15
    python scripts/fetch_github.py --repo owner/name
"""
import io, os, re, sys, json, argparse, datetime, urllib.request, urllib.error

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

SCRIPTS = os.path.dirname(os.path.abspath(__file__))
VAULT   = os.path.dirname(SCRIPTS)
RAW     = os.path.join(VAULT, "raw")
REPO_DEFAULT = "Nikola1Davydov/AnalyzeTool"
API = "https://api.github.com"


def get(url):
    req = urllib.request.Request(url, headers={
        "Accept": "application/vnd.github+json",
        "User-Agent": "analysetool-llm-wiki-ingest",
    })
    with urllib.request.urlopen(req, timeout=60) as r:
        return json.load(r)


def paged(path, repo, cap=20):
    """Все страницы по 100 записей."""
    out = []
    for page in range(1, cap + 1):
        sep = "&" if "?" in path else "?"
        chunk = get(f"{API}/repos/{repo}/{path}{sep}per_page=100&page={page}")
        if not chunk:
            break
        out += chunk
        print(f"   страница {page}: {len(chunk)}")
        if len(chunk) < 100:
            break
    return out


def guard(path):
    if os.path.exists(path):
        print(f"!! {os.path.basename(path)} уже существует — не перезаписываю (raw/ неизменяем).")
        print("   Нужен свежий снимок — задайте другую дату: --date ГГГГ-ММ-ДД")
        return False
    return True


def write_issues(repo, date):
    out = os.path.join(RAW, f"github-issues-{date}.md")
    if not guard(out):
        return
    print("Скачиваю issues…")
    data = [i for i in paged("issues?state=all", repo) if "pull_request" not in i]
    data.sort(key=lambda i: i["number"])
    op = sum(1 for i in data if i["state"] == "open")
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(f"# Снимок issues — {repo}\n\n")
        f.write(f"Скачано {date} через публичный API (`/issues?state=all`), pull request'ы исключены.\n")
        f.write(f"{len(data)} issue: {op} открытых, {len(data)-op} закрытых.\n")
        f.write("Неизменяемый снимок — не править. Комментарии НЕ включены.\n\n---\n\n")
        for i in data:
            lab = ", ".join(l["name"] for l in i["labels"]) or "-"
            f.write(f"## #{i['number']} — {i['title']}\n\n")
            f.write(f"состояние: {i['state']} | метки: {lab} | создано: {i['created_at'][:10]} | комментариев: {i['comments']}\n")
            f.write(f"{i['html_url']}\n\n")
            f.write(((i.get("body") or "").strip() or "_(без тела)_") + "\n\n---\n\n")
    print(f"OK  {out}  ({len(data)} issue, {os.path.getsize(out)} байт)")


def write_comments(repo, date):
    out = os.path.join(RAW, f"github-issue-comments-{date}.md")
    if not guard(out):
        return
    print("Скачиваю комментарии…")
    data = paged("issues/comments", repo)
    by = {}
    for c in data:
        n = int(re.search(r"/issues/(\d+)$", c["issue_url"]).group(1))
        by.setdefault(n, []).append(c)
    with io.open(out, "w", encoding="utf-8") as f:
        f.write(f"# Комментарии к issues — снимок {repo}\n\n")
        f.write(f"Скачано {date} через публичный API (`/issues/comments`).\n")
        f.write(f"{len(data)} комментариев на {len(by)} issue. Неизменяемый снимок — не править.\n\n")
        f.write("Помните: комментарии здесь — не обсуждение, а РЕВИЗИИ ПЛАНА.\n")
        f.write("Тело issue часто описывает более раннее состояние мысли, чем комментарий под ним.\n\n---\n\n")
        for n in sorted(by):
            f.write(f"## #{n}\n\nhttps://github.com/{repo}/issues/{n}\n\n")
            for c in sorted(by[n], key=lambda x: x["created_at"]):
                f.write(f"### {c['user']['login']} · {c['created_at'][:16]}\n\n")
                f.write((c.get("body") or "").strip() + "\n\n")
            f.write("---\n\n")
    print(f"OK  {out}  ({len(data)} комментариев на {len(by)} issue, {os.path.getsize(out)} байт)")


def main():
    ap = argparse.ArgumentParser(description="снимки трекера в raw/")
    ap.add_argument("--repo", default=REPO_DEFAULT)
    ap.add_argument("--date", default=datetime.date.today().isoformat())
    ap.add_argument("--issues", action="store_true")
    ap.add_argument("--comments", action="store_true")
    a = ap.parse_args()
    both = not (a.issues or a.comments)
    try:
        if a.issues or both:
            write_issues(a.repo, a.date)
        if a.comments or both:
            write_comments(a.repo, a.date)
    except urllib.error.HTTPError as e:
        print(f"HTTP {e.code}: {e.reason}")
        if e.code == 403:
            print("Похоже на лимит запросов без токена (60/час). Подождите или задайте токен.")
        return 1
    print("\nДальше — операция ingest из CLAUDE.md: страница в sources/, вплетение, index, log.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
