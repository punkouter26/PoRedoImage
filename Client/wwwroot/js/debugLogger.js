// Debug Logger - Captures browser console and network activity
class DebugLogger {
    constructor() {
        this.logs = [];
        this.networkLogs = [];
        this.maxLogs = 1000; // Prevent memory issues
        this.isCapturing = true;
        
        this.setupConsoleCapture();
        this.setupNetworkCapture();
        this.setupErrorCapture();
        this.startPeriodicFlush();
        
        console.log('Debug Logger initialized - capturing console and network activity');
    }

    setupConsoleCapture() {
        // Store original console methods
        const originalConsole = {
            log: console.log.bind(console),
            warn: console.warn.bind(console),
            error: console.error.bind(console),
            info: console.info.bind(console),
            debug: console.debug.bind(console)
        };

        // Override console methods
        ['log', 'warn', 'error', 'info', 'debug'].forEach(method => {
            console[method] = (...args) => {
                // Call original method first
                originalConsole[method](...args);
                
                // Capture for debug log
                if (this.isCapturing) {
                    this.addLog('console', method, args.map(arg => 
                        typeof arg === 'object' ? JSON.stringify(arg, null, 2) : String(arg)
                    ).join(' '));
                }
            };
        });
    }

    setupNetworkCapture() {
        // Capture fetch requests
        const originalFetch = window.fetch;
        window.fetch = async (...args) => {
            const startTime = performance.now();
            const [url, options = {}] = args;
            
            this.addNetworkLog('REQUEST', {
                url: url.toString(),
                method: options.method || 'GET',
                headers: options.headers || {},
                timestamp: new Date().toISOString()
            });

            try {
                const response = await originalFetch(...args);
                const endTime = performance.now();
                
                this.addNetworkLog('RESPONSE', {
                    url: url.toString(),
                    status: response.status,
                    statusText: response.statusText,
                    duration: Math.round(endTime - startTime),
                    timestamp: new Date().toISOString()
                });
                
                return response;
            } catch (error) {
                const endTime = performance.now();
                
                this.addNetworkLog('ERROR', {
                    url: url.toString(),
                    error: error.message,
                    duration: Math.round(endTime - startTime),
                    timestamp: new Date().toISOString()
                });
                
                throw error;
            }
        };

        // Capture XMLHttpRequest
        const originalXHR = XMLHttpRequest;
        window.XMLHttpRequest = function() {
            const xhr = new originalXHR();
            const self = window.debugLogger;
            
            const originalOpen = xhr.open;
            const originalSend = xhr.send;
            let requestData = null;
            
            xhr.open = function(method, url, ...args) {
                requestData = { method, url, timestamp: new Date().toISOString() };
                return originalOpen.apply(this, [method, url, ...args]);
            };
            
            xhr.send = function(data) {
                if (self && self.isCapturing) {
                    self.addNetworkLog('XHR_REQUEST', {
                        ...requestData,
                        data: data ? String(data).substring(0, 200) + '...' : null
                    });
                }
                
                const startTime = performance.now();
                
                xhr.onreadystatechange = function() {
                    if (xhr.readyState === 4 && self && self.isCapturing) {
                        const endTime = performance.now();
                        self.addNetworkLog('XHR_RESPONSE', {
                            url: requestData.url,
                            status: xhr.status,
                            statusText: xhr.statusText,
                            duration: Math.round(endTime - startTime),
                            timestamp: new Date().toISOString()
                        });
                    }
                };
                
                return originalSend.apply(this, arguments);
            };
            
            return xhr;
        };
    }

    setupErrorCapture() {
        // Capture unhandled errors
        window.addEventListener('error', (event) => {
            this.addLog('error', 'unhandled', `${event.message} at ${event.filename}:${event.lineno}:${event.colno}`);
        });

        // Capture unhandled promise rejections
        window.addEventListener('unhandledrejection', (event) => {
            this.addLog('error', 'promise', `Unhandled promise rejection: ${event.reason}`);
        });
    }

    addLog(source, level, message) {
        if (!this.isCapturing) return;
        
        const logEntry = {
            timestamp: new Date().toISOString(),
            source,
            level,
            message,
            url: window.location.href,
            userAgent: navigator.userAgent
        };
        
        this.logs.push(logEntry);
        
        // Prevent memory issues
        if (this.logs.length > this.maxLogs) {
            this.logs = this.logs.slice(-this.maxLogs);
        }
    }

    addNetworkLog(type, data) {
        if (!this.isCapturing) return;
        
        const logEntry = {
            timestamp: new Date().toISOString(),
            type,
            ...data
        };
        
        this.networkLogs.push(logEntry);
        
        // Prevent memory issues
        if (this.networkLogs.length > this.maxLogs) {
            this.networkLogs = this.networkLogs.slice(-this.maxLogs);
        }
    }

    startPeriodicFlush() {
        // Flush logs to file every 5 seconds
        setInterval(() => {
            this.flushToFile();
        }, 5000);
        
        // Also flush on page unload
        window.addEventListener('beforeunload', () => {
            this.flushToFile();
        });
    }

    async flushToFile() {
        if (this.logs.length === 0 && this.networkLogs.length === 0) return;
        
        const debugData = {
            consoleLogs: [...this.logs],
            networkLogs: [...this.networkLogs],
            performance: this.getPerformanceData(),
            browserInfo: this.getBrowserInfo()
        };
        
        try {
            // Send to server endpoint - format matches ClientLogEntry model
            await fetch('/api/log/client', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({
                    message: `Debug log flush: ${this.logs.length} console logs, ${this.networkLogs.length} network logs`,
                    level: 'Information',
                    timestamp: new Date().toISOString(),
                    url: window.location.href,
                    sessionId: this.getSessionId(),
                    properties: {
                        debugData: JSON.stringify(debugData)
                    }
                })
            });
            
            // Clear logs after successful flush
            this.logs = [];
            this.networkLogs = [];
            
        } catch (error) {
            // Don't log to console as it would create infinite loop
            // Just silently fail - debug logging is non-critical
        }
    }

    getSessionId() {
        let sessionId = sessionStorage.getItem('debug-session-id');
        if (!sessionId) {
            sessionId = 'session-' + Date.now() + '-' + Math.random().toString(36).substr(2, 9);
            sessionStorage.setItem('debug-session-id', sessionId);
        }
        return sessionId;
    }

    getPerformanceData() {
        if (!window.performance) return null;
        
        return {
            timing: window.performance.timing,
            navigation: window.performance.navigation,
            memory: window.performance.memory ? {
                usedJSHeapSize: window.performance.memory.usedJSHeapSize,
                totalJSHeapSize: window.performance.memory.totalJSHeapSize,
                jsHeapSizeLimit: window.performance.memory.jsHeapSizeLimit
            } : null
        };
    }

    getBrowserInfo() {
        return {
            userAgent: navigator.userAgent,
            language: navigator.language,
            platform: navigator.platform,
            cookieEnabled: navigator.cookieEnabled,
            onLine: navigator.onLine,
            screen: {
                width: screen.width,
                height: screen.height,
                colorDepth: screen.colorDepth
            },
            viewport: {
                width: window.innerWidth,
                height: window.innerHeight
            }
        };
    }

    // Public methods for manual debugging
    startCapture() {
        this.isCapturing = true;
        console.log('Debug capture started');
    }

    stopCapture() {
        this.isCapturing = false;
        console.log('Debug capture stopped');
    }

    clearLogs() {
        this.logs = [];
        this.networkLogs = [];
        console.log('Debug logs cleared');
    }

    downloadLogs() {
        const debugData = {
            timestamp: new Date().toISOString(),
            session: this.getSessionId(),
            consoleLogs: this.logs,
            networkLogs: this.networkLogs,
            performance: this.getPerformanceData(),
            browserInfo: this.getBrowserInfo()
        };
        
        const blob = new Blob([JSON.stringify(debugData, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `debug-logs-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }
}

// Initialize debug logger when script loads
window.debugLogger = new DebugLogger();

// Add some convenience methods to window for manual debugging
window.debugStart = () => window.debugLogger.startCapture();
window.debugStop = () => window.debugLogger.stopCapture();
window.debugClear = () => window.debugLogger.clearLogs();
window.debugDownload = () => window.debugLogger.downloadLogs();
window.debugFlush = () => window.debugLogger.flushToFile();
