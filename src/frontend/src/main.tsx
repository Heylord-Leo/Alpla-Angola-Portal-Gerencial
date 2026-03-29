import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App.tsx';
import './styles/globals.css';

import { logger } from './lib/logger';

window.addEventListener('error', (event) => {
    logger.log({
        level: 'Error',
        eventType: 'RUNTIME_ERROR',
        message: event.message || 'Uncaught runtime error',
        exception: event.error,
        componentKey: 'Global'
    });
});

window.addEventListener('unhandledrejection', (event) => {
    logger.log({
        level: 'Error',
        eventType: 'UNHANDLED_REJECTION',
        message: event.reason?.message || 'Unhandled promise rejection',
        exception: event.reason,
        componentKey: 'Global'
    });
});

ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
        <App />
    </React.StrictMode>,
);
