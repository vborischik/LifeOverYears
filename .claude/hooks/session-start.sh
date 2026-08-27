#!/bin/bash
# Staleness guard, run before the session does any work.
#
# More than one person commits to this repo. A local main that a previous
# session pushed itself is not evidence that it is still current, and nobody —
# human or agent — reliably remembers to check. Assuming it was current is how
# the motel work got built on a base an hour and a half stale, costing a merge,
# a BuildSceneBlock parameter both sides had added, and a renumbered check.
#
# So the check does not depend on anyone remembering: it runs here, and the
# result is in front of whoever starts the session. Deliberately non-blocking —
# it reports, it does not refuse to let work begin. An unreachable origin is a
# warning, not a failure, so an offline session still starts.
set -uo pipefail

cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

if ! git fetch --quiet origin main 2>/dev/null; then
    echo "⚠ SessionStart: could not reach origin — the base may be stale and this was not verified."
    exit 0
fi

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "?")
behind=$(git rev-list --count HEAD..origin/main 2>/dev/null || echo 0)
ahead=$(git rev-list --count origin/main..HEAD 2>/dev/null || echo 0)

if [ "$behind" -gt 0 ]; then
    echo "⚠ SessionStart: '$branch' is $behind commit(s) BEHIND origin/main. Rebase or merge before"
    echo "  starting work — building on this base means a merge later, and check numbers or shared"
    echo "  function signatures picked now may already be taken upstream. Commits you would miss:"
    git log --oneline --no-decorate -10 HEAD..origin/main | sed 's/^/    /'
else
    echo "✓ SessionStart: '$branch' is up to date with origin/main (ahead $ahead)."
fi

# Warm the build so the first smoke run is not paying for a cold restore.
# Non-fatal: a restore failure must not stop the session from starting.
if command -v dotnet >/dev/null 2>&1; then
    dotnet restore src/LifeOverYears >/dev/null 2>&1 \
        && echo "✓ SessionStart: dotnet restore complete." \
        || echo "⚠ SessionStart: dotnet restore failed — run it by hand before building."
fi

exit 0
