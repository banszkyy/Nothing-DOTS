import * as vscode from 'vscode'
import {
    LanguageClient,
    LanguageClientOptions,
    Disposable
} from 'vscode-languageclient/node'
import * as utils from './utils'
import { log } from './extension'
import * as net from 'net'

export let client: LanguageClientManager | null = null

export function activate(context: vscode.ExtensionContext) {
    startServer(context)

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.server.restart`, () => {
        restartServer(context)
    }))
}

export function deactivate() {
    stopServer()
}

function restartServer(context: vscode.ExtensionContext) {
    return stopServer().then(() => startServer(context))
}

function startServer(context: vscode.ExtensionContext) {
    client = new LanguageClientManager(context)
    return client.activate()
}

function stopServer() {
    if (!client) return Promise.resolve()
    return client.deactivate()
        .finally(() => {
            client?.dispose()
            client = null
        })
}

export type LanguageClientManagerOptions = {
    serverPath: string,
    args?: string[],
}

export class LanguageClientManager implements Disposable {
    readonly client: LanguageClient
    private readonly context: vscode.ExtensionContext
    private readonly outputChannel: vscode.LogOutputChannel

    constructor(context: vscode.ExtensionContext, args: string[] = []) {
        this.outputChannel = vscode.window.createOutputChannel('Nothingame Language Server', { log: true })

        const connectionOptions: net.NetConnectOpts = {
            host: '127.0.0.1',
            port: 8052,
        }

        const clientOptions: LanguageClientOptions = {
            documentSelector: [{
                language: ".vi",
            }],
            synchronize: {
                fileEvents: [
                    vscode.workspace.createFileSystemWatcher('**/.vi')
                ],
            },
            outputChannel: this.outputChannel,
        }

        this.client = new LanguageClient(
            utils.extensionId,
            'Nothingame Language Client',
            () => {
                log.debug(`[Language] [TCP] Connecting to ${connectionOptions.host}:${connectionOptions.port}`)
                const socket = net.connect(connectionOptions)
                socket.on('connect', () => log.info(`[Language] [TCP] Connected to ${socket.remoteAddress}:${socket.remotePort}`))
                socket.on('error', (error) => log.error(`[Language] [TCP] ${error}`))
                socket.on('connectionAttempt', (ip, port, family) => log.trace(`[Language] [TCP] Connection attempt to ${ip}:${port} ${family}`))
                socket.on('connectionAttemptFailed', (ip, port, family, error) => log.trace(`[Language] [TCP] Connection attempt to ${ip}:${port} ${family} failed ${error}`))
                socket.on('connectionAttemptTimeout', (ip, port, family) => log.trace(`[Language] [TCP] Connection attempt to ${ip}:${port} ${family} timed out`))
                socket.on('close', () => log.info(`[Language] [TCP] Socket closed`))
                return Promise.resolve({
                    writer: socket,
                    reader: socket,
                })
            },
            clientOptions
        )

        this.client.onNotification('window/logMessage', (message) => {
            switch (message.type) {
                case 1:
                    this.outputChannel.error(message.message)
                    break
                case 2:
                    this.outputChannel.warn(message.message)
                    break
                case 3:
                    this.outputChannel.info(message.message)
                    break
                case 4:
                    this.outputChannel.appendLine(message.message)
                    break
                case 5:
                    this.outputChannel.debug(message.message)
                    break
                case 6:
                    this.outputChannel.trace(message.message)
                    break
                default:
                    this.outputChannel.appendLine(message.message)
                    break
            }
        })

        this.client.error = () => { }
        this.client.warn = () => { }
        this.client.info = () => { }
        this.client.debug = () => { }

        log.debug(`[Language] Language server created`)

        this.context = context
    }

    public activate(): Promise<void> {
        log.debug(`[Language] Starting language server ...`)
        return this.client.start()
            .then(() => {
                this.context.subscriptions.push(this.client)
                log.debug(`[Language] Language server started`)
            })
            .catch(error => {
                log.error(`[Language] Failed to start language server`, error)
                vscode.window.showErrorMessage(error)
            })
    }

    public deactivate(): Promise<void> {
        log.debug(`[Language] Stopping language server ...`)
        return this.client?.stop()
            .then(() => {
                log.debug(`[Language] Language server stopped`)
            })
            .catch(error => {
                log.error(`[Language] Failed to stop language server`, error)
                vscode.window.showErrorMessage(error)
            })
    }

    [Symbol.dispose]() { this.dispose() }

    public dispose() {
        this.client?.dispose()
        this.outputChannel?.dispose()
        this.outputChannel.dispose()
        log.error(`[Language] Disposed`)
    }
}
