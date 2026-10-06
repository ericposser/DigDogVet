<div align="center">

# 🐾 DigDog Vet

**Sistema web de gestão para clínicas veterinárias e pet shops**

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-ASP.NET%20Core%20MVC-239120?style=for-the-badge&logo=csharp&logoColor=white)
![MySQL](https://img.shields.io/badge/MySQL-Pomelo%20EF%20Core-4479A1?style=for-the-badge&logo=mysql&logoColor=white)
![SignalR](https://img.shields.io/badge/SignalR-tempo%20real-00B4AB?style=for-the-badge)

</div>

---

## ✨ Funcionalidades

| Módulo | Descrição |
|---|---|
| 🐶 **Clientes e Pets** | Cadastro de tutores e seus animais, com carteira de vacinação e histórico |
| 🩺 **Consultas** | Agendamento e registro de consultas veterinárias |
| 💉 **Vacinação** | Controle de vacinas aplicadas e calendário de vacinação |
| 🛁 **Banho & Tosa** | Agendamento e acompanhamento de serviços de banho e tosa |
| 📄 **Receituário** | Emissão de receitas com geração de PDF (QuestPDF) |
| 🛒 **Produtos e Vendas** | Catálogo de produtos e controle de vendas |
| 💳 **Assinaturas** | Planos recorrentes com webhook (Cakto) e notificações em tempo real via SignalR |
| 📋 **Kanban de Tickets** | Quadro em tempo real integrado ao Trello |
| 👥 **Usuários & Permissões** | Autenticação via ASP.NET Identity, papéis e permissões customizadas |
| 🧾 **Logs** | Registro e limpeza automática de logs do sistema |
| 📅 **Status do Dia** | Painel de acompanhamento diário das atividades |

---

## 🛠️ Stack

- ⚙️ **.NET 8** — ASP.NET Core MVC + Razor Pages
- 🗄️ **Entity Framework Core** — Pomelo MySQL (com suporte a SQLite/SQL Server)
- 🔐 **ASP.NET Identity** — autenticação e autorização
- 🔄 **SignalR** — atualizações em tempo real (Kanban, assinaturas)
- 📑 **QuestPDF** — geração de documentos (receituários)
- 🔗 **Integrações externas** — Cakto (webhooks de pagamento) e Trello (quadro de tickets)

---

## 📂 Estrutura do projeto

```
DigDog/
├── 🎮 Controllers/     # Controllers MVC (Cliente, Pet, Consulta, Venda, Assinatura, ...)
├── 🧩 Models/          # Entidades, ViewModels, Enums e Configurações
├── 🖼️ Views/           # Views Razor
├── 💾 Data/            # DbContext (Contexto.cs)
├── ⚡ Services/        # Regras de negócio e integrações (Trello, Cakto, Log, Permissão)
├── 📡 Hubs/            # Hubs SignalR (Kanban, Assinatura)
├── 🧱 Middlewares/     # Middlewares customizados
├── 🛡️ Filters/         # Filtros de autorização/permissão
└── 🔑 Areas/Identity/  # Páginas de autenticação (ASP.NET Identity)
```

---

<div align="center">

**Projeto privado — todos os direitos reservados.**

</div>
