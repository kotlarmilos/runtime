---
name: "Mobile Platform Failure Scanner"
description: "Daily scan of the runtime-extra-platforms pipeline for Apple mobile and Android failures. Investigates and proposes fixes."

permissions:
  contents: read
  issues: read
  pull-requests: read

if: github.event.repository.fork == false

on:
  schedule: daily
  workflow_dispatch:
  roles: [admin, maintainer, write]

# ###############################################################
# Override the COPILOT_GITHUB_TOKEN secret usage for the workflow
# with a randomly-selected token from a pool of secrets.
#
# As soon as organization-level billing is offered for Agentic
# Workflows, this stop-gap approach will be removed.
#
# See: /.github/actions/select-copilot-pat/README.md
# ###############################################################

  # Add the pre-activation step of selecting a random PAT from the supplied secrets
  steps:
    - uses: actions/checkout@de0fac2e4500dabe0009e67214ff5f5447ce83dd # v6.0.2
      name: Checkout the select-copilot-pat action folder
      with:
        persist-credentials: false
        sparse-checkout: .github/actions/select-copilot-pat
        sparse-checkout-cone-mode: true
        fetch-depth: 1

    - id: select-copilot-pat
      name: Select Copilot token from pool
      uses: ./.github/actions/select-copilot-pat
      env:
        SECRET_0: ${{ secrets.COPILOT_PAT_0 }}
        SECRET_1: ${{ secrets.COPILOT_PAT_1 }}
        SECRET_2: ${{ secrets.COPILOT_PAT_2 }}
        SECRET_3: ${{ secrets.COPILOT_PAT_3 }}
        SECRET_4: ${{ secrets.COPILOT_PAT_4 }}
        SECRET_5: ${{ secrets.COPILOT_PAT_5 }}
        SECRET_6: ${{ secrets.COPILOT_PAT_6 }}
        SECRET_7: ${{ secrets.COPILOT_PAT_7 }}
        SECRET_8: ${{ secrets.COPILOT_PAT_8 }}
        SECRET_9: ${{ secrets.COPILOT_PAT_9 }}

# Add the pre-activation output of the randomly selected PAT
jobs:
  pre-activation:
    outputs:
      copilot_pat_number: ${{ steps.select-copilot-pat.outputs.copilot_pat_number }}

# Override the COPILOT_GITHUB_TOKEN expression used in the activation job
# Consume the PAT number from the pre-activation step and select the corresponding secret
engine:
  id: copilot
  model: claude-sonnet-4.5
  env:
    # We cannot use line breaks in this expression as it leads to a syntax error in the compiled workflow
    # If none of the `COPILOT_PAT_#` secrets were selected, then the default COPILOT_GITHUB_TOKEN is used
    COPILOT_GITHUB_TOKEN: ${{ case(needs.pre_activation.outputs.copilot_pat_number == '0', secrets.COPILOT_PAT_0, needs.pre_activation.outputs.copilot_pat_number == '1', secrets.COPILOT_PAT_1, needs.pre_activation.outputs.copilot_pat_number == '2', secrets.COPILOT_PAT_2, needs.pre_activation.outputs.copilot_pat_number == '3', secrets.COPILOT_PAT_3, needs.pre_activation.outputs.copilot_pat_number == '4', secrets.COPILOT_PAT_4, needs.pre_activation.outputs.copilot_pat_number == '5', secrets.COPILOT_PAT_5, needs.pre_activation.outputs.copilot_pat_number == '6', secrets.COPILOT_PAT_6, needs.pre_activation.outputs.copilot_pat_number == '7', secrets.COPILOT_PAT_7, needs.pre_activation.outputs.copilot_pat_number == '8', secrets.COPILOT_PAT_8, needs.pre_activation.outputs.copilot_pat_number == '9', secrets.COPILOT_PAT_9, secrets.COPILOT_GITHUB_TOKEN) }}

concurrency:
  group: "mobile-scan"
  cancel-in-progress: true

tools:
  github:
    toolsets: [pull_requests, repos, issues, search]
  edit:
  bash: ["dotnet", "git", "find", "ls", "cat", "grep", "head", "tail", "wc", "curl", "jq", "pwsh"]

checkout:
  fetch-depth: 50

safe-outputs:
  create-pull-request:
    title-prefix: "[mobile] "
    draft: true
    max: 2
    protected-files: fallback-to-issue
  create-issue:
    max: 2
  add-comment:
    max: 5
    target: "*"

timeout-minutes: 60

network:
  allowed:
    - defaults
    - dev.azure.com
    - helix.dot.net
---

# Mobile Platform Failure Scanner

You scan the `runtime-extra-platforms` pipeline (AzDO definition 154, org `dnceng-public`, project `public`) for Apple mobile and Android failures on `main`, triage them, and propose fixes.

**Data safety:** Do not include secrets, tokens, internal URLs, machine credentials, or personally identifiable information in PR descriptions, issue comments, or commit messages. Sanitize log excerpts before posting: redact paths containing usernames, environment variables with secrets, and authentication headers.

## Step 1: Load domain knowledge

Read `.github/skills/mobile-platforms/SKILL.md`.

## Step 2: Get the latest build ID

```bash
BUILD_ID=$(curl -s "https://dev.azure.com/dnceng-public/public/_apis/build/builds?definitions=154&branchName=refs/heads/main&statusFilter=completed&\$top=1&api-version=7.1" | jq '.value[0].id')
BUILD_RESULT=$(curl -s "https://dev.azure.com/dnceng-public/public/_apis/build/builds?definitions=154&branchName=refs/heads/main&statusFilter=completed&\$top=1&api-version=7.1" | jq -r '.value[0].result')
echo "Build $BUILD_ID result: $BUILD_RESULT"
```

If `BUILD_RESULT` is `succeeded`, stop -- nothing to fix.

## Step 3: Analyze failures with ci-analysis

Use the ci-analysis skill script to get structured failure data:

```bash
pwsh .github/skills/ci-analysis/scripts/Get-CIStatus.ps1 -BuildId $BUILD_ID -ShowLogs
```

The script fetches the build timeline, extracts failed jobs, retrieves Helix work item failures and console logs, checks for known build errors, and emits a `[CI_ANALYSIS_SUMMARY]` JSON block. Parse the JSON summary to get structured failure details including `errorCategory`, `errorSnippet`, and `helixWorkItems` for each failed job.

## Step 4: Filter to mobile failures

From the ci-analysis output, keep only failures whose job names match mobile platforms:

- Apple mobile: `ios`, `tvos`, `maccatalyst`, `ioslike`, `ioslikesimulator`
- Android: `android`

Ignore failures in non-mobile jobs. If no mobile jobs failed, stop.

## Step 5: Triage each failure

Classify each mobile failure as **infrastructure** or **code** using the criteria from the skill document and the `errorCategory` from ci-analysis output.

If ci-analysis already matched a failure to a **known build error** issue, add a comment to that issue with the build details and skip the fix attempt.

**Infrastructure failures:** Report them on existing tracking issues (with a machine/build table entry) or create a new issue if no similar one exists. Use appropriate labels: `area-Infrastructure` plus `os-ios`, `os-tvos`, `os-maccatalyst`, or `os-android`.

**Code failures:** Check recent commits (`git log --oneline --since='3 days ago'`), trace the root cause, fix it, and create a draft PR.

## Step 6: Submit

Before creating a PR, search for existing open PRs that already fix the same issue. If one exists, do not create a duplicate -- add a comment on the issue noting the existing PR instead.

For each fix, create a draft PR referencing the issue. Post a comment on the issue with root cause analysis and a link to the PR.

If you learned something new during investigation that would help future triage, record it as a comment on the issue (or create a new issue if none exists) so the team can later incorporate it into `.github/skills/mobile-platforms/SKILL.md`.
