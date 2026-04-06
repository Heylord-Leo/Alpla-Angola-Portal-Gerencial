import { Component, ErrorInfo, ReactNode } from 'react';

interface Props {
    children: ReactNode;
    fallbackName?: string;
}

interface State {
    hasError: boolean;
    error: Error | null;
    errorInfo: ErrorInfo | null;
}

export class ErrorBoundary extends Component<Props, State> {
    public state: State = {
        hasError: false,
        error: null,
        errorInfo: null
    };

    public static getDerivedStateFromError(error: Error): Partial<State> {
        return { hasError: true, error };
    }

    public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
        console.error(`[ErrorBoundary][${this.props.fallbackName || 'Unknown'}] Caught render error:`, error, errorInfo);
        this.setState({ errorInfo });
    }

    public render() {
        if (this.state.hasError) {
            return (
                <div style={{
                    padding: '32px',
                    margin: '20px',
                    backgroundColor: '#FEF2F2',
                    border: '2px solid #EF4444',
                    borderRadius: '8px',
                    fontFamily: 'monospace'
                }}>
                    <h2 style={{ color: '#EF4444', margin: '0 0 16px 0', fontSize: '1.1rem' }}>
                        ⚠️ Render Error in {this.props.fallbackName || 'Component'}
                    </h2>
                    <pre style={{
                        backgroundColor: '#1F2937',
                        color: '#F87171',
                        padding: '16px',
                        borderRadius: '4px',
                        overflow: 'auto',
                        fontSize: '0.8rem',
                        maxHeight: '300px'
                    }}>
                        {this.state.error?.toString()}
                        {'\n\n'}
                        {this.state.errorInfo?.componentStack}
                    </pre>
                    <button
                        onClick={() => window.location.reload()}
                        style={{
                            marginTop: '16px',
                            padding: '8px 16px',
                            backgroundColor: '#EF4444',
                            color: 'white',
                            border: 'none',
                            borderRadius: '4px',
                            cursor: 'pointer',
                            fontWeight: 700
                        }}
                    >
                        Recarregar Página
                    </button>
                </div>
            );
        }

        return this.props.children;
    }
}
