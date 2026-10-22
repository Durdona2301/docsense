# DocSense

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

Confirmed: .NET 10, PostgreSQL/pgvector, PDF/Markdown/TXT ingestion and a local web workspace. AI integration uses a local Ollama service. The interface is served by ASP.NET Core with HTML, CSS and JavaScript.

## Users

Developers and people exploring a small personal document collection. The repository is a public portfolio project; the app runs as a local single-user workspace.

## Product Purpose

Upload documents, ask questions, and inspect the exact source passages behind answers.

## Capabilities and Constraints

- PDF, Markdown and UTF-8 text ingestion, chunking and persistent indexing.
- Grounded answers with inspectable sources, and an explicit no-evidence state.
- Optional local model integration; key-free extractive search remains explicitly labeled.
- A sample document enables a first question without private data.
- This version is a trusted local workspace, without shared hosting or multi-user authorization.

## Brand Commitments

DocSense is the confirmed name. English UI and documentation are the initial portfolio default. The user selected image-first design before implementation.

## Evidence on Hand

No existing product, branding or user claims. Sample documents must be visibly labeled examples.

## Product Principles

- Evidence should be easy to inspect.
- Be explicit about model and fallback modes.
- Keep local setup reproducible and secrets out of source.
