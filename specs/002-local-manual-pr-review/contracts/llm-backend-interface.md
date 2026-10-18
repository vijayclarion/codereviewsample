# Contract: LLM backend interface

**Feature**: `002-local-manual-pr-review` | Satisfies FR-030 – FR-034 | Research R7, R8

This is the contract the FR-033 audit measures the agent against. It exists so that "the agent is
LLM-agnostic" is a property a test can assert, not a claim in a README.

---

## The interface

A backend is an object with two methods and two attributes. Nothing else.

```python
class Backend:
    name: str                 # registry key, e.g. "copilot"
    requires_key: bool = True # False for a local runtime such as ollama
    requires_base_url: bool = False

    def build_request(self, prompt: str, config) -> tuple[str, dict, dict]:
        """Return (url, headers, json_body) for one completion call."""

    def extract_text(self, payload: dict) -> str:
        """Return the model's raw text from a successful response body."""
```

Registration is one line:

```python
BACKENDS = {b.name: b for b in (Claude(), OpenAI(), Azure(), Ollama(), Copilot())}
```

## What the interface deliberately does not include

| Not in the interface | Why |
|---|---|
| Retry, backoff, timeout | Provider-neutral policy; lives once in `call_llm` |
| Prompt construction | `prompt_builder.py` — must never learn a provider name (FR-032) |
| Response parsing beyond text extraction | `parser.py` receives raw text and stays provider-blind |
| Streaming, tool use, multi-turn | Not needed; a review is one prompt in, one text out |
| Error classification | `call_llm` maps transport and status outcomes to `DegradedReview` / `SetupFailure` |

`extract_text` returning `""` is a normal outcome, handled as an unusable response by `parser.py`'s existing
one-retry-then-degrade rule — not an exception the backend raises.

## Adding a provider — the FR-032 checklist

A new provider requires exactly:

1. One new `Backend` object in `llm_adapter.py`.
2. One entry in `BACKENDS`.
3. One documented line in `.env.example`.

It must require **no** change to: `main.py`, `run.py`, `prompt_builder.py`, `parser.py`, `checklist*.py`,
`output_layer.py`, `selection.py`, `dedupe.py`, `tfs_client.py`, or any test that is not the new backend's
own unit test.

**Enforced by** `tests/contract/test_backend_interface.py` (new):

- every registered backend satisfies the interface shape;
- `BACKENDS` is the only place a provider name appears outside `llm_adapter.py` — a repo-wide scan for the
  known provider names finds no hit in any other module (this is the FR-033 audit, automated);
- the setup-failure message for an unknown provider is generated from `BACKENDS.keys()`, so it cannot drift;
- a synthetic `FakeBackend` registered at test time flows end to end through the review with no change to
  any other module — the executable proof of FR-032.

## Registered backends

| Key | Endpoint shape | Auth header | Base URL |
|---|---|---|---|
| `claude` | `POST {base}/v1/messages` | `x-api-key` + `anthropic-version` | defaulted |
| `openai` | `POST {base}/v1/chat/completions` | `Authorization: Bearer` | defaulted |
| `azure` | `POST {base}/openai/deployments/{model}/chat/completions?api-version=…` | `api-key` | **required** |
| `ollama` | `POST {base}/api/generate` | none (`requires_key = False`) | **required** |
| `copilot` | `POST {base}/chat/completions` | `Authorization: Bearer` (header style configurable) | **required** |

### Note on `copilot`

"Copilot" names several products with different endpoints — GitHub Models (OpenAI-compatible,
token-authenticated), Copilot-branded Azure OpenAI deployments (already served by `azure`), and the IDE
Copilot Chat service (no supported general completions API). All of the supported ones speak the OpenAI
chat-completions shape, so the backend is that shape with a required, configurable base URL.

**Open environment fact**: the exact endpoint and credential for this organisation's Copilot entitlement must
be confirmed with the platform owner before the FR-031 acceptance run (workstream F). If it turns out to be
an Azure OpenAI deployment, `LLM_PROVIDER=azure` already satisfies FR-031 with no code at all. The cost of
being wrong is one registry object — which is the point of the interface.
