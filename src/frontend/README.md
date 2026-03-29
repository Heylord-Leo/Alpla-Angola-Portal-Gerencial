# Alpla Angola - Portal Gerencial (Frontend V1)

Este diretório contém a interface de usuário (Frontend) da aplicação, construída com **React**, **Vite**, e **TypeScript**, conectando-se ao backend em ASP.NET Core.

## Requisitos

- **Node.js** (v18 ou superior recomendado)
- **NPM** (Gerenciador padrão)

## Como rodar localmente

1. **Instale as dependências**
   Abra um terminal na pasta `src/frontend` e rode:

   ```bash
   npm install
   ```

2. **Configure o `.env`**
   Copie `.env.example` para `.env.development` e garanta que o backend aponta para o endereço correto (exemplo: `http://localhost:5000`).

3. **Inicie o Servidor de Desenvolvimento**

   ```bash
   npm run dev
   ```

   **URL Local Esperada:** `http://localhost:5173/`

### Rodando o ecossistema completo (2 Terminais)

Para testar a integração com o banco de dados e APIs:

1. Terminal 1: `cd src/backend` -> `dotnet run --project AlplaPortal.Api --launch-profile http` (Porta 5000)
2. Terminal 2: `cd src/frontend` -> `npm run dev` (Porta 5173)
