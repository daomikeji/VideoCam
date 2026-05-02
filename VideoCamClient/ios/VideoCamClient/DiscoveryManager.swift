import Foundation

class DiscoveryManager {

    private let port: Int
    private var socket: CFSocket?
    private var discoveredInfo: (ip: String, tcp: Int, udp: Int)?

    init(port: Int) {
        self.port = port
    }

    func discover() throws -> (ip: String, tcp: Int, udp: Int) {
        let semaphore = DispatchSemaphore(value: 0)
        var result: (ip: String, tcp: Int, udp: Int)?
        var error: Error?

        DispatchQueue.global().async {
            do {
                try self.setupSocket()

                // Wait for server response with timeout
                DispatchQueue.global().asyncAfter(deadline: .now() + 5.0) {
                    semaphore.signal()
                }

                semaphore.wait()

                if let info = self.discoveredInfo {
                    result = info
                } else {
                    error = NSError(domain: "Discovery", code: -1, userInfo: [NSLocalizedDescriptionKey: "Server discovery timeout"])
                }
            } catch {
                error = error
                semaphore.signal()
            }
        }

        semaphore.wait()

        if let error = error {
            throw error
        }

        guard let result = result else {
            throw NSError(domain: "Discovery", code: -1, userInfo: [NSLocalizedDescriptionKey: "No server found"])
        }

        return result
    }

    private func setupSocket() throws {
        var context = CFSocketContext()
        context.info = Unmanaged.passUnretained(self).toOpaque()

        let socket = CFSocketCreate(
            kCFAllocatorDefault,
            PF_INET,
            SOCK_DGRAM,
            IPPROTO_UDP,
            CFSocketCallBackType.readCallBack.rawValue,
            { (socket, type, address, data, info) in
                if let info = info {
                    let manager = Unmanaged<DiscoveryManager>.fromOpaque(info).takeUnretainedValue()
                    manager.handleDiscoveryResponse(data)
                }
            },
            &context
        )

        guard let socket = socket else {
            throw NSError(domain: "Socket", code: -1, userInfo: [NSLocalizedDescriptionKey: "Failed to create socket"])
        }

        // Bind to port
        var address = sockaddr_in()
        address.sin_family = sa_family_t(AF_INET)
        address.sin_port = in_port_t(port).bigEndian
        address.sin_addr.s_addr = htonl(INADDR_ANY)

        let addressData = NSData(bytes: &address, length: MemoryLayout<sockaddr_in>.size)
        CFSocketSetAddress(socket, addressData as CFData)

        // Add to run loop
        let runLoopSource = CFSocketCreateRunLoopSource(kCFAllocatorDefault, socket, 0)
        CFRunLoopAddSource(CFRunLoopGetCurrent(), runLoopSource, .defaultMode)

        self.socket = socket
    }

    private func handleDiscoveryResponse(_ data: CFData?) {
        guard let data = data as Data? else { return }

        let response = String(data: data, encoding: .utf8) ?? ""
        let parts = response.split(separator: ",").map(String.init)

        if parts.count >= 3,
           let tcp = Int(parts[1]),
           let udp = Int(parts[2]) {
            discoveredInfo = (ip: parts[0], tcp: tcp, udp: udp)
        }
    }

    deinit {
        if let socket = socket {
            CFSocketInvalidate(socket)
        }
    }
}
