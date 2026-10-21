import * as vscode from 'vscode'
import * as utils from './utils'
import { log } from './extension'

export function activate(context: vscode.ExtensionContext) {
    vscode.commands.executeCommand('setContext', `${utils.extensionId}.isProfiling`, false)

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.profiling.start`, () => {
        let w = vscode.debug.activeDebugSession
        while (w) {
            log.info(`[Profiling] Starting profiling...`)
            w.customRequest('startProfiling', {})
                .then(res => {
                    log.info(`[Profiling] Profiling started`, res)
                    vscode.commands.executeCommand('setContext', `${utils.extensionId}.isProfiling`, true)
                })
            w = w.parentSession
        }
    }))

    context.subscriptions.push(vscode.commands.registerCommand(`${utils.extensionId}.profiling.stop`, () => {
        if (!vscode.debug.activeDebugSession || vscode.debug.activeDebugSession.type !== utils.extensionId) {
            log.info(`[Profiling] No active debug session found. Please start a debug session first.`)
            return
        }

        log.info(`[Profiling] Stopping profiling...`)
        vscode.debug.activeDebugSession.customRequest('stopProfiling', {})
            .then(res => {
                log.info(`[Profiling] Profiling stopped`, res)
                vscode.commands.executeCommand('setContext', `${utils.extensionId}.isProfiling`, false)

                if (res.Path) {
                    //log.info(`[Profiling] Opening ${vscode.Uri.file(res.Path)}`)
                    //vscode.commands.executeCommand('workbench.action.files.openFile', vscode.Uri.file(res.Path))
                } else {
                    vscode.window.showErrorMessage(`Profiling failed: No path returned from the debug adapter.`)
                }
            })
    }))
}
