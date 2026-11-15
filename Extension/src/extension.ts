import * as vscode from 'vscode'
import * as debuggerClient from './debugger-client'
import * as profiling from './profiling'
import * as utils from './utils'
import * as net from 'net'
import * as rpc from 'vscode-jsonrpc/node'
import { connectAsync, connectManyAsync } from './tcp'
import { DebugSessionHub } from './debugger-hub'

export let log: vscode.LogOutputChannel
export let token: string | null = null

export type GameUnit = {
    id: string,
    name: string,
    signal: string,
    source: string | null,
}

let serverConnection: net.Socket | null = null
export let serverConnectionRpc: rpc.MessageConnection | null = null
export let debuggerHub: DebugSessionHub | null = null

export function activate(context: vscode.ExtensionContext) {
    log = vscode.window.createOutputChannel("Nothingame Extension", { log: true })

    const connectionStatusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Right)
    context.subscriptions.push(connectionStatusItem)

    connectionStatusItem.show()
    connectionStatusItem.text = `$(remote) Disconnected`
    connectionStatusItem.command = `${utils.extensionId}.authenticate`
    connectionStatusItem.tooltip = 'Authenticate'

    const authenticateClient = async () => {
        const socket = await connectAsync('127.0.0.1', 8051)
        context.subscriptions.push({ dispose() { socket.destroy() } })

        const connection = rpc.createMessageConnection(
            new rpc.StreamMessageReader(socket),
            new rpc.StreamMessageWriter(socket))
        context.subscriptions.push(connection)

        connection.listen()

        try {
            log.debug(`[ExtClient] Authenticating ...`)
            const authRes = await connection.sendRequest<any>('authenticate')
            log.debug(JSON.stringify(authRes))
            if (authRes.success) {
                return String(authRes.token)
            } else {
                throw new Error(`Access denied`)
            }
        } finally {
            connection.end()
            connection.dispose()
            socket.end(() => {
                socket.destroy()
            })
        }
    }

    const authenticateServer = async (token: string) => {
        log.info(`Authenticated with`, token)

        log.info(`Connecting to server extension host ...`)
        let portTry = 8052
        let tries = 10
        const socket = await connectManyAsync('127.0.0.1', () => tries++ <= 0 ? undefined : portTry++)
        context.subscriptions.push({ dispose() { socket.destroy() } })

        log.info(`Connected to server extension host ${socket.remoteAddress}:${socket.remotePort}`)

        const connectionRpc = rpc.createMessageConnection(new rpc.StreamMessageReader(socket), new rpc.StreamMessageWriter(socket))
        context.subscriptions.push(connectionRpc)
        const newDebuggerHub = new DebugSessionHub(connectionRpc, 'nothingame')
        context.subscriptions.push(newDebuggerHub)

        log.info(`Authenticating on the server host with ${token} ...`)
        const authRes = await connectionRpc.sendRequest('authenticate', token)
        log.info(`Authenticated on the server host`, authRes)

        unitsTreeDataProvider.refresh()

        if (debuggerHub) {
            debuggerHub.dispose()
            debuggerHub = null
        }

        if (serverConnectionRpc) {
            serverConnectionRpc.dispose()
            serverConnectionRpc = null
        }

        if (serverConnection) {
            serverConnection.destroy()
            serverConnection = null
        }

        serverConnection = socket
        serverConnectionRpc = connectionRpc
        debuggerHub = newDebuggerHub

        connectionStatusItem.show()
        connectionStatusItem.text = `$(remote) Connected`
        connectionStatusItem.tooltip = undefined
        connectionStatusItem.command = undefined

        serverConnection.once('close', () => {
            debuggerHub?.dispose()
            debuggerHub = null

            serverConnectionRpc?.dispose()
            serverConnectionRpc = null
            serverConnection = null

            connectionStatusItem.show()
            connectionStatusItem.text = `$(remote) Disconnected`
            connectionStatusItem.command = `${utils.extensionId}.authenticate`
            connectionStatusItem.tooltip = 'Authenticate'
        })

        serverConnectionRpc.onClose(() => serverConnection?.destroy())
    }

    const authenticate = async () => {
        if (!token) {
            token = await authenticateClient()
        }
        await authenticateServer(token)
    }

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.units.refresh`, () => {
        unitsTreeDataProvider.refresh()
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.authenticate.internal`, (_token: string) => {
        token = _token
        authenticateServer(token)
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.authenticate`, () => {
        const _auth = () => authenticate()
            .catch(error => {
                log.error(error)
                vscode.window.showErrorMessage(`Authentication failed. See logs for details`, 'Retry', 'View Logs')
                    .then(res => {
                        if (res === 'Retry') {
                            _auth()
                        } else if (res === 'View Logs') {
                            log.show(false)
                        }
                    })
            })

        _auth()
    }))

    context.subscriptions.push(vscode.window.registerUriHandler({
        async handleUri(uri) {
            log.trace(`Handling uri ${uri} ...`)

            const url = URL.parse(uri.toString(true))
            if (!url) {
                log.warn(`Invalid uri ${uri}`)
                vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}"`)
                return
            }

            switch (url.pathname) {
                case '/debug': {
                    const ghost = url.searchParams.get('ghost')
                    const _token = url.searchParams.get('token')
                    const name = url.searchParams.get('name') ?? `Unit ${ghost}`

                    if (!ghost) {
                        log.warn(`Invalid URI ${uri.toString()}: Parameter "ghost" is required`)
                        vscode.window.showErrorMessage(`Invalid URI ${uri.toString()}: Parameter "ghost" is required`)
                        break
                    }

                    if (!_token) {
                        log.warn(`Invalid URI ${uri.toString()}: Parameter "token" is required`)
                        vscode.window.showErrorMessage(`Invalid URI ${uri.toString()}: Parameter "token" is required`)
                        break
                    }

                    if (token !== _token) {
                        token = _token
                        await authenticateServer(token)
                    }

                    log.debug(`[Debugger] Try to start debugging ghost ${ghost} ...`)

                    debuggerClient.startDebugging(_token, ghost, name)
                    break
                }
                case '/authenticate': {
                    const _token = url.searchParams.get('token')

                    if (!_token) {
                        log.warn(`Invalid URI "${uri.toString()}": Parameter "token" is required`)
                        vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Parameter "token" is required`)
                        break
                    }

                    token = _token
                    await authenticateServer(token)
                    break
                }
                default:
                    vscode.window.showErrorMessage(`Invalid URI "${uri.toString()}": Unknown pathname "${url.pathname}"`)
                    break
            }
        },
    }))

    const unitsTreeDataProvider = new class implements vscode.TreeDataProvider<GameUnit> {
        constructor() { }

        private _onDidChangeTreeData = new vscode.EventEmitter<GameUnit | undefined>();
        readonly onDidChangeTreeData = this._onDidChangeTreeData.event;

        refresh(): void {
            this._onDidChangeTreeData.fire(undefined)
        }

        getTreeItem(element: GameUnit): vscode.TreeItem {
            const res = new vscode.TreeItem(element.name, vscode.TreeItemCollapsibleState.None)
            res.tooltip = element.source ?? 'No source'
            res.contextValue = `${utils.extensionId}.entity.${element.signal}`
            res.iconPath = ({
                //'off': { id: '', color: { id: 'disabledForeground' } },
                'debugged': new vscode.ThemeIcon('debug', new vscode.ThemeColor('debugIcon.disconnectForeground')),
                'running': new vscode.ThemeIcon('check', new vscode.ThemeColor('gauge.foreground')),
                'halted': new vscode.ThemeIcon('debug-pause', new vscode.ThemeColor('gauge.warningForeground')),
                'crashed': new vscode.ThemeIcon('warning', new vscode.ThemeColor('gauge.errorForeground')),
            })[element.signal]
            if (element.source && (element.source.startsWith('/') || element.source.startsWith('\\'))) {
                res.resourceUri = vscode.Uri.file(element.source)
                res.command = {
                    command: 'vscode.open',
                    title: 'Open File',
                    arguments: [element.source],
                }
            }
            return res
        }

        async getChildren(element?: GameUnit): Promise<GameUnit[]> {
            if (!element) {
                if (!serverConnectionRpc) return []
                if (!token) return []
                const units = await serverConnectionRpc.sendRequest<GameUnit[]>('units', { token: token })
                units.sort((a, b) => a.id < b.id ? -1 : a.id > b.id ? 1 : 0)
                return units
            }
            return []
        }
    }()
    context.subscriptions.push(vscode.window.createTreeView('gameUnits', { treeDataProvider: unitsTreeDataProvider }))

    debuggerClient.activate(context)
    profiling.activate(context)
}

export function deactivate() {
    debuggerClient.deactivate()
}
