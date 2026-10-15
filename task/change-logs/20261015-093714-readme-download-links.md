# README download links

## Request

Add download links to README.

## Changes

Added a Download section near the top of README with version 1.3.1 links for Windows Setup, Windows Portable, Linux x64 and Windows checksums. Included artifact extraction/startup instructions, GitHub sign-in requirements, the current artifact expiration date and a link to the build workflow for newer builds.

## Verification and limits

Queried GitHub's API: build run 38666513610 succeeded and all four linked artifacts exist, are not expired and have an expiration date of January 13, 2027. Reviewed the Markdown table and link targets; `git diff --check` passed. Documentation-only change; no application build required. Artifact URLs require GitHub sign-in and remain subject to retention or manual deletion.

## Release status

README points to the existing verified 1.3.1 packages. This change does not create or republish binaries.
