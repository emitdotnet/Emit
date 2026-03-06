# Emit Docs

Documentation site built with Starlight (Astro). Instructions for writing and maintaining content.

## Diagrams

- **Always use the `<Mermaid>` component** for any diagram or flow — never ASCII art.
- Import it at the top of the file: `import Mermaid from '../../../components/Mermaid.astro';`
  (adjust the relative path depth as needed).
- Pass the Mermaid syntax via the `code` prop as a template literal.

```mdx
<Mermaid code={`flowchart LR
    A["Step 1"] --> B["Step 2"]`} />
```

## Tone

Experienced programmer, professional but human, occasional dry humor. Not a tutorial farm.
Don't over-explain. Assume the reader is a capable .NET developer.

## Typography

- **No em-dashes (—).** Ever. Use a comma, semicolon, colon, or period instead.
  - "term — definition" becomes "term: definition"
  - "clause — clause" becomes "clause; clause" or split into two sentences
  - "aside — like this — inline" becomes "(like this)" with parentheses

## Provider-agnostic language

Emit will support more messaging providers beyond Kafka. Outside of Kafka-specific sections:
- Say "message broker", "messaging provider", or "provider" — not "Kafka"
- Say "delivery confirmed" — not "Kafka acknowledgement"
- Say "publish/produce a message" — not "publish to Kafka"

Kafka-specific sections (`/kafka/*`) may use Kafka terminology freely.

## Page structure

- Use `<Steps>` from `@astrojs/starlight/components` for sequential procedures.
- Keep pages focused — one concept per page, not exhaustive references.
- Use `:::note`, `:::tip`, `:::caution` admonitions sparingly and only when they add real value.

## Sync rule

Keep docs in sync with code. When APIs change, update the corresponding `/docs` pages.
