import Foundation
import AVFoundation
import React

@objc(VideoStreamingModule)
class VideoStreamingModule: NSObject {

    private var cameraManager: CameraManager?
    private var streamingManager: StreamingManager?
    private var isFrontCamera = false
    private var isFlashOn = false

    override init() {
        super.init()
        self.cameraManager = CameraManager()
        self.streamingManager = StreamingManager()
    }

    @objc
    func startDiscovery(_ port: NSNumber, withResolver resolve: @escaping RCTPromiseResolveBlock, andRejecter reject: @escaping RCTPromiseRejectBlock) {
        DispatchQueue.global().async {
            do {
                let discoveryManager = DiscoveryManager(port: port.intValue)
                let (ip, tcp, udp) = try discoveryManager.discover()

                resolve([
                    "success": true,
                    "ip": ip,
                    "tcp": tcp,
                    "udp": udp
                ])
            } catch {
                reject("DISCOVERY_ERROR", error.localizedDescription, error)
            }
        }
    }

    @objc
    func connectAndStream(_ ip: String, tcpPort: NSNumber, udpPort: NSNumber, withResolver resolve: @escaping RCTPromiseResolveBlock, andRejecter reject: @escaping RCTPromiseRejectBlock) {
        DispatchQueue.global().async {
            do {
                try self.streamingManager?.connect(
                    toIP: ip,
                    tcpPort: tcpPort.intValue,
                    udpPort: udpPort.intValue
                )
                try self.cameraManager?.startCapture()

                resolve([
                    "success": true,
                    "message": "Connected and streaming"
                ])
            } catch {
                reject("CONNECTION_ERROR", error.localizedDescription, error)
            }
        }
    }

    @objc
    func disconnect(withResolver resolve: @escaping RCTPromiseResolveBlock, andRejecter reject: @escaping RCTPromiseRejectBlock) {
        DispatchQueue.global().async {
            self.cameraManager?.stopCapture()
            self.streamingManager?.disconnect()
            resolve(true)
        }
    }

    @objc
    func sendControlCommand(_ command: String, withResolver resolve: @escaping RCTPromiseResolveBlock, andRejecter reject: @escaping RCTPromiseRejectBlock) {
        DispatchQueue.global().async {
            do {
                if command.starts(with: "SWITCH_CAMERA:") {
                    let cameraMode = String(command.dropFirst("SWITCH_CAMERA:".count))
                    try self.switchCamera(toFront: cameraMode == "FRONT")
                } else if command.starts(with: "TOGGLE_FLASH:") {
                    let flashMode = String(command.dropFirst("TOGGLE_FLASH:".count))
                    try self.toggleFlash(on: flashMode == "ON")
                }

                try self.streamingManager?.sendCommand(command)
                resolve(true)
            } catch {
                reject("COMMAND_ERROR", error.localizedDescription, error)
            }
        }
    }

    private func switchCamera(toFront: Bool) throws {
        try self.cameraManager?.switchCamera(toFront: toFront)
        self.isFrontCamera = toFront
    }

    private func toggleFlash(on: Bool) throws {
        if self.isFrontCamera {
            print("Front camera doesn't support flash")
            return
        }
        try self.cameraManager?.toggleFlash(on: on)
        self.isFlashOn = on
    }

    @objc
    static func requiresMainQueueSetup() -> Bool {
        return true
    }
}
