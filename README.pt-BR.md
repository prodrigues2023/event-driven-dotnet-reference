# Arquitetura de Referência Orientada a Eventos em .NET

> Mensageria confiável entre serviços em .NET — outbox transacional, consumidores idempotentes,
> retry com dead-letter e sagas. Documentada primeiro, implementada em público.

[![Fase](https://img.shields.io/badge/fase-1%20arquitetura-blue)](./ROADMAP.md)
[![ADRs](https://img.shields.io/badge/ADRs-5-green)](./docs/adr)
[![Licença](https://img.shields.io/badge/licen%C3%A7a-MIT-lightgrey)](./LICENSE)

Publicar uma mensagem em .NET leva cinco linhas. Publicar de forma *confiável* — sem perder a
mensagem quando o banco comita e a chamada ao broker falha, sem processar duas vezes quando o
consumidor cai depois de tratar e antes de confirmar, e sem descartar em silêncio uma mensagem
envenenada — é o trabalho de verdade. É nessa lacuna que a maioria dos sistemas orientados a
eventos falha em produção: meses depois do lançamento e sempre no pior momento.

Este repositório documenta as decisões que fecham essa lacuna e, sobre elas, constrói uma
implementação de referência.

**English:** [README.md](./README.md)

---

## O que já existe

| Área | Status | Link |
| --- | --- | --- |
| Contexto e escopo | Pronto | [docs/context.md](./docs/context.md) |
| Diagramas C4 e fluxos de mensagens | Pronto | [docs/diagrams](./docs/diagrams) |
| Registros de Decisão de Arquitetura | 5 publicados | [docs/adr](./docs/adr) |
| Atributos de qualidade e trade-offs | Pronto | [docs/quality-attributes.md](./docs/quality-attributes.md) |
| Implementação de referência | Planejada — Fase 3 | [ROADMAP.md](./ROADMAP.md) |

## Os quatro problemas que esta arquitetura resolve

| Problema | O que dá errado | Tratado por |
| --- | --- | --- |
| **Dual write** | A transação comita, a publicação falha, a mensagem se perde para sempre | [ADR-0003 — Outbox transacional](./docs/adr/0003-transactional-outbox.md) |
| **Entrega duplicada** | Entrega at-least-once significa que todo consumidor verá a mesma mensagem duas vezes, mais cedo ou mais tarde | [ADR-0004 — Consumidores idempotentes](./docs/adr/0004-idempotent-consumers.md) |
| **Mensagem envenenada** | Uma mensagem improcessável trava a fila, ou é descartada sem rastro | [ADR-0005 — Retry e dead-lettering](./docs/adr/0005-retry-and-dead-lettering.md) |
| **Topologia acoplada** | Adicionar um consumidor exige mudar o produtor | [ADR-0002 — Topologia](./docs/adr/0002-messaging-topology.md) |

> Os documentos técnicos são mantidos em inglês para alcançar o público mais amplo possível.
> Este README traz o contexto em português.

## Por que documentar primeiro

Os modos de falha acima são baratos de projetar e caríssimos de corrigir depois. Um sistema que
publica sem outbox não falha em teste — falha sob carga, em produção, meses depois, e a perda é
silenciosa. Registrar as decisões antes torna os trade-offs revisáveis enquanto ainda dá para
mudá-los.

## Roadmap

Quatro fases, acompanhadas como milestones no GitHub. Detalhes em [ROADMAP.md](./ROADMAP.md).

1. **Arquitetura** — contexto, diagramas, ADRs, atributos de qualidade
2. **Contratos** — schemas de mensagens, versionamento, sagas, catálogo de falhas
3. **Implementação de referência** — publisher, consumidores, outbox, saga host, Docker Compose
4. **Resiliência e operação** — testes de caos, observabilidade, runbooks, teste de carga

## Relacionados

- [rag-reference-architecture](https://github.com/prodrigues2023/rag-reference-architecture) — RAG em cargas corporativas
- [ai-solution-architecture-kit](https://github.com/prodrigues2023/ai-solution-architecture-kit) — artefatos de governança de arquitetura

## Autor

Paulo Roberto Franco Rodrigues — Solutions Architect.
Vinte anos em sistemas distribuídos; mais de uma década projetando integração assíncrona com
RabbitMQ e .NET, e plataformas Kubernetes com a observabilidade necessária para operá-las.
[LinkedIn](https://linkedin.com/in/paulo-roberto-franco-rodrigues)

## Licença

MIT — veja [LICENSE](./LICENSE).
