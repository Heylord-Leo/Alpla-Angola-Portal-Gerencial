import { API_BASE_URL } from './api';

export type LogLevel = 'Information' | 'Warning' | 'Error';

export type FrontendComponentKey = 'Global' | 'OcrSettings' | 'AdminApi' | 'SyncApi';

export type FrontendEventType = 
    | 'RUNTIME_ERROR' 
    | 'UNHANDLED_REJECTION' 
    | 'API_REQUEST_FAILED' 
    | 'OCR_SETTINGS_UI_ERROR' 
    | 'FRONTEND_EVENT';

interface LogPayload {
    level: LogLevel;
    eventType: FrontendEventType;
    message: string;
    componentKey: FrontendComponentKey;
    route?: string;
    endpoint?: string;
    statusCode?: number;
    correlationId?: string;
    clientEventId?: string;
    userAgent?: string;
    exceptionDetail?: string;
}

const CLIENT_EVENT_ID_KEY = 'alpla_client_event_id';
let clientEventId = localStorage.getItem(CLIENT_EVENT_ID_KEY);
if (!clientEventId) {
    clientEventId = Math.random().toString(36).substring(2, 15) + Math.random().toString(36).substring(2, 15);
    localStorage.setItem(CLIENT_EVENT_ID_KEY, clientEventId);
}

export const logger = {
    log: async (params: {
        level: LogLevel;
        eventType: FrontendEventType;
        message: string;
        componentKey?: FrontendComponentKey;
        exception?: any;
        endpoint?: string;
        statusCode?: number;
        correlationId?: string;
    }) => {
        try {
            const payload: LogPayload = {
                level: params.level,
                eventType: params.eventType,
                message: params.message.substring(0, 500),
                componentKey: params.componentKey || 'Global',
                route: window.location.pathname,
                endpoint: params.endpoint,
                statusCode: params.statusCode,
                correlationId: params.correlationId,
                clientEventId: clientEventId || undefined,
                userAgent: navigator.userAgent.substring(0, 512),
                exceptionDetail: params.exception ? 
                    (params.exception.stack || String(params.exception)).substring(0, 2000) : 
                    undefined
            };

            // Use fetch directly to avoid circular dependency with api.ts if we were to use the api object
            fetch(`${API_BASE_URL}/api/admin/logs/ingest`, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-Dev-User': localStorage.getItem('dev_user_email') || 'system@alpla.com' // Aligned with AdminLogWriter fallback
                },
                body: JSON.stringify(payload)
            }).catch(err => {
                console.warn('Failed to send frontend log to backend', err);
            });
        } catch (e) {
            console.warn('Error in frontend logger', e);
        }
    },

    error: (message: string, exception?: any, componentKey?: FrontendComponentKey) => {
        logger.log({ level: 'Error', eventType: 'RUNTIME_ERROR', message, exception, componentKey });
    },

    warn: (message: string, componentKey?: FrontendComponentKey) => {
        logger.log({ level: 'Warning', eventType: 'FRONTEND_EVENT', message, componentKey });
    },

    info: (message: string, componentKey?: FrontendComponentKey) => {
        logger.log({ level: 'Information', eventType: 'FRONTEND_EVENT', message, componentKey });
    }
};
