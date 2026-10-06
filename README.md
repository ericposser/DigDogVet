# DigDog Vet

Sistema web de gestão para clínicas veterinárias e pet shops (banho & tosa, consultas, vacinação, vendas, assinaturas e muito mais), construído em **ASP.NET Core MVC (.NET 8)**.

## Funcionalidades

- **Clientes e Pets** — cadastro de tutores e seus animais, com carteira de vacinação e histórico.
- **Consultas** — agendamento e registro de consultas veterinárias.
- **Vacinação** — controle de vacinas aplicadas e calendário de vacinação (`Vacina` / `Vacinacao`).
- **Banho & Tosa** — agendamento e acompanhamento de serviços de banho e tosa.
- **Receituário** — emissão de receitas (geração de PDF via QuestPDF).
- **Produtos e Vendas** — catálogo de produtos e controle de vendas.
- **Assinaturas** — gerenciamento de planos recorrentes, com integração de webhook (Cakto) e notificações em tempo real via SignalR (`AssinaturaHub`).
- **Kanban de tickets** — quadro de tickets em tempo real (`KanbanHub`), com integração ao Trello.
- **Usuários, Roles e Permissões** — autenticação via ASP.NET Identity, controle de acesso por papéis e permissões customizadas.
- **Logs** — registro e limpeza automática de logs do sistema.
- **Painel de status do dia** — acompanhamento diário das atividades.

## Stack

- **.NET 8** / ASP.NET Core MVC + Razor Pages
- **Entity Framework Core** (Pomelo MySQL, com suporte a SQLite/SQL Server)
- **ASP.NET Identity** para autenticação e autorização
- **SignalR** para atualizações em tempo real (Kanban, assinaturas)
- **QuestPDF** para geração de documentos (receituários)
- Integrações externas: **Cakto** (webhooks de pagamento/assinatura) e **Trello** (quadro de tickets)

## Estrutura do projeto

```
DigDog/
├── Controllers/     # Controllers MVC (Cliente, Pet, Consulta, Venda, Assinatura, ...)
├── Models/          # Entidades, ViewModels, Enums e Configurações
├── Views/           # Views Razor
├── Data/            # DbContext (Contexto.cs)
├── Services/        # Regras de negócio e integrações (Trello, Cakto, Log, Permissão)
├── Hubs/            # Hubs SignalR (Kanban, Assinatura)
├── Middlewares/      # Middlewares customizados
├── Filters/         # Filtros de autorização/permissão
└── Areas/Identity/  # Páginas de autenticação (ASP.NET Identity)
```

## Licença

Projeto privado — todos os direitos reservados.
