# Bills Manager – Blazor Server + ASP.NET Core API  
A modern, fully featured billing management system built with **Blazor Server**, **ASP.NET Core Web API**, and **Bootstrap 5**.  
This project provides a clean admin-style UI for managing bills, viewing analytics, and interacting with a live-updating dashboard.

---

## 🧭 Frontend Options

This project includes a **Blazor Server frontend** by default, offering a modern, component-driven C# web UI.

However, developers who prefer **React** can also build a React-based frontend for this backend.
Follow the link to the react README.md
- [BillsFrontEndBlazor README](bills-frontend/FrontEndReact/README.md)

## 🚀 Setup & Run
Follow these steps to run both the frontend (Blazor Server) and backend (ASP.NET Core API) locally.

🔧 1. Clone the Repository
```
git clone https://github.com/IbrahimKhatibDev/billManagementApi.git
cd billManagementApi
```

📦 2. Restore Dependencies
```
dotnet restore
```

🖥️ 3. Run the Backend API
Navigate to your API project:
```
cd BillsFrontEndBlazor
dotnet run
```

🌐 4. Run the Frontend (Blazor Server)
```
cd BillsAPI
dotnet run
```

## 🚀 Features

### 🖥️ **Dashboard (Home Page)**
- Live-updating analytics (no timers, event-driven)
- Total bills count  
- Paid bills count  
- Outstanding amount  
- Responsive Bootstrap cards  
- Hero banner and quick-action tiles  

### 📄 **Bills Management Page**
A complete CRUD interface built with Blazor + Bootstrap:

- Create Bill (modal with validation)  
- Edit Bill (modal with validation)  
- Delete Bill (confirmation modal)  
- Inline validation messages  
- Search by ID or Payee  
- Success/error alerts  
- Styled table with badges and action buttons  

### 🔄 **Real-Time UI Updates**
A custom **BillEventService** notifies all pages when bills change.  
This gives you:

- Live dashboard updates  
- Reactive UI  
- No polling or timers  
- Clean architecture  

### 🏗️ **Backend API**
A RESTful ASP.NET Core API (`BillDtos` endpoint)  
Supports:

- GET all bills  
- POST create bill  
- PUT update bill  
- DELETE remove bill  

### 🎨 **UI & Styling**
- Fully responsive Bootstrap 5 UI  
- Icons from Bootstrap Icons  
- Admin-dashboard design  
- Styled modals, cards, alerts, tables  

## 🧩 **Tech Stack**

### Frontend
- **Blazor Server (.NET 8)**  
- **Bootstrap 5.3**  
- **Bootstrap Icons**  
- **Blazor EditForm validation**  
- **Event-driven updates using a pub/sub service**

### Backend
- **ASP.NET Core Web API (.NET 8)**  
- **HttpClient-based BillService**  
- **Models shared across UI + API**  

---

## 📡 **Event-Driven Updates**

A singleton service is used to notify components:

```csharp
public class BillEventService
{
    public event Action? OnBillsChanged;
    public void NotifyBillsChanged() => OnBillsChanged?.Invoke();
}
