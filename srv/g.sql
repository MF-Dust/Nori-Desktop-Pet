INSERT INTO services (name, base_path)
VALUES ('nori', '/nori');

SET @service_id = LAST_INSERT_ID();

INSERT INTO service_nodes (service_id, node_url)
VALUES (@service_id, 'http://192.168.21.3:8083');

INSERT INTO routes (path, method, service_id)
VALUES ('/ping', 'GET', @service_id),
	   ('/resource/download_url', 'GET', @service_id);
