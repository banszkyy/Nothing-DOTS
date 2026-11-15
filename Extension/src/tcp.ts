import * as net from 'net'
import { log } from './extension'

export async function connectManyAsync(address: string, ports: ReadonlyArray<number> | (() => number | undefined)) {
    let errors = []
    if (typeof ports !== 'function') {
        let i = 0
        const portsList = ports
        ports = () => portsList[i++]
    }
    let port = ports()
    while (port) {
        try {
            return await connectAsync(address, port)
        } catch (error) {
            errors.push(error)
        }
        port = ports()
    }

    if (errors.length === 0) throw new Error(`Connection attempt failed to ${address} (tried multiple ports)`)
    else throw errors[0]
}

export function connectAsync(address: string, port: number) {
    return new Promise<net.Socket>((resolve, reject) => {
        log.debug(`[TCP] [${address}:${port}] Connecting ...`)
        let resolved = false
        const socket = net.connect({
            host: address,
            port: port,
        })
        socket.on('connect', () => {
            log.trace(`[TCP] [${address}:${port}] Connected to ${socket.remoteAddress}:${socket.remotePort}`)
            resolved = true
            resolve(socket)
        })
        socket.on('error', (error) => {
            log.trace(`[TCP] [${address}:${port}] ${error}`)
            resolved = true
            reject(error)
        })
        socket.on('connectionAttempt', (ip, port, family) => log.trace(`[TCP] [${address}:${port}] Connection attempt to ${ip}:${port}`))
        socket.on('connectionAttemptFailed', (ip, port, family, error) => log.trace(`[TCP] [${address}:${port}] Connection attempt to ${ip}:${port} failed ${error}`))
        socket.on('connectionAttemptTimeout', (ip, port, family) => log.trace(`[TCP] [${address}:${port}] Connection attempt to ${ip}:${port} timed out`))
        socket.on('close', () => {
            log.trace(`[TCP] [${address}:${port}] Socket closed`)
            if (resolved) return
            resolved = true
            reject(new Error("Socket closed"))
        })
    })
}
